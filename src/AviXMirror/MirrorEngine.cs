using System.Diagnostics;
using AviXMirror.Capture;
using AviXMirror.Output;
using AviXMirror.Radar;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Orchestration : recherche de LMU, capture, radar ou caméra AC, ATH, LEDs et affichage sur le VoCore.
/// Toutes les méthodes publiques s'appellent sur le thread UI.
/// </summary>
public sealed class MirrorEngine : IDisposable
{
    readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer _standbyTimer = new() { Interval = 33 };
    readonly Stopwatch _standbyClock = Stopwatch.StartNew();

    Settings _master = new();   // réglages communs + profils de tous les jeux
    Settings _settings = new(); // réglages effectifs pour le jeu en cours
    WgcCapture? _capture;
    IntPtr _captureWindow;
    volatile bool _captureClosed;
    RadarSource? _radar;
    MaskForm? _mask;
    VoCoreUsbOutput? _usb;
    AcCameraSource? _acCamera;
    Spotter? _spotter;
    Hud.HudOverlay? _hud;
    GameActivity? _activity;
    GameState _state = GameState.NoGame;
    bool _stateShown;

    /// <summary>État du jeu (au volant, pause, bureau…), qui décide de l'écran de veille.</summary>
    public GameState State => _state;

    Action<Bitmap>? _hudPostProcess;
    Size? _upscale;
    bool _calibrating;

    /// <summary>
    /// Vrai pendant la calibration : l'image brute du jeu reste affichée (sans ATH ni écran de veille),
    /// même si le jeu est en pause.
    /// </summary>
    public bool Calibrating
    {
        get => _calibrating;
        set
        {
            _calibrating = value;
            Frames.PostProcess = value ? null : _hudPostProcess;
            Frames.UpscaleTo = value ? null : _upscale;
            if (Running)
                UpdateState(_settings);
        }
    }

    /// <summary>Couleurs des LEDs du spotter (gauche, droite) pour l'aperçu, ou vides.</summary>
    public (Color[] Left, Color[] Right) LedSides => _spotter?.Sides ?? (Array.Empty<Color>(), Array.Empty<Color>());

    IntPtr _gameWindow;
    IntPtr _gameSeenWindow;
    readonly Stopwatch _gameSeen = new();

    public FrameBuffer Frames { get; } = new();
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Arrêté.";
    public WgcCapture? Capture => _capture;

    /// <summary>Voitures derrière, dans le repère du joueur, telles que l'ATH les place (null sans ATH ou sans télémétrie).</summary>
    public List<Hud.HudTarget>? HudTargets() => _hud?.CurrentTargets();

    /// <summary>Jeu dont le profil de réglages est appliqué (null : réglages communs).</summary>
    public RadarGame? CurrentGame { get; private set; }

    /// <summary>Réglages effectivement appliqués (profil du jeu en cours).</summary>
    public Settings Effective => _settings;

    public MirrorEngine()
    {
        _watchdog.Tick += (_, _) => Tick();
        _standbyTimer.Tick += (_, _) => DrawStandby();
    }

    public void Start(Settings settings)
    {
        Stop();
        _master = settings.Clone();
        CurrentGame = DetectGame(_master) ?? CurrentGame;
        _settings = EffectiveFor(CurrentGame);
        Running = true;

        _usb = new VoCoreUsbOutput(Frames, _settings);

        var size = _usb.LogicalSize;
        _activity = new GameActivity();
        _state = GameState.NoGame;
        _stateShown = false;

        // ATH par-dessus les images des modes caméra et capture (le radar dessine le sien).
        if (_settings.Mode is MirrorMode.CameraAssettoCorsa or MirrorMode.Capture)
        {
            _hud = new Hud.HudOverlay(_settings, _settings.Mode);
            var hud = _hud;
            _hudPostProcess = bmp => hud.Draw(bmp);
            Frames.PostProcess = _hudPostProcess;
            // Capture LMU : ATH dessiné en pleine résolution de l'écran, par-dessus l'image agrandie.
            if (_settings.Mode == MirrorMode.Capture)
            {
                _upscale = size;
                Frames.UpscaleTo = size;
            }
        }

        if (_settings.LedsEnabled)
        {
            _spotter = new Spotter(_settings);
            var usb = _usb;
            _spotter.Changed += chain => usb.SetLeds(chain);
        }

        if (_settings.Mode == MirrorMode.Radar)
            _radar = new RadarSource(Frames, _settings) { OutputSize = _usb.LogicalSize };
        else if (_settings.Mode == MirrorMode.CameraAssettoCorsa)
            _acCamera = new AcCameraSource(Frames, _settings);

        Tick();
        _watchdog.Start();
    }

    /// <summary>Applique des réglages sans redémarrer (luminosité, caméra, radar…).</summary>
    public void UpdateLive(Settings settings)
    {
        if (!Running)
            return;
        _master = settings.Clone();
        ApplyEffective(EffectiveFor(CurrentGame));
    }

    /// <summary>Réglages du profil d'un jeu ; en mode Auto, la télémétrie suit ce jeu.</summary>
    Settings EffectiveFor(RadarGame? game)
    {
        var effective = _master.ForGame(game);
        if (game is { } g && _master.RadarGame == RadarGame.Auto)
            effective.RadarGame = g;
        return effective;
    }

    /// <summary>Jeu dont le profil doit s'appliquer : le jeu choisi, ou en Auto le premier lancé.</summary>
    static RadarGame? DetectGame(Settings master)
    {
        if (master.RadarGame != RadarGame.Auto)
            return master.RadarGame;
        foreach (var game in Games.All)
            if (Games.IsRunning(game, master))
                return game.Game;
        return null;
    }

    /// <summary>Change de jeu en cours de route : nouveau profil, redémarrage seulement si le mode change.</summary>
    void FollowGame()
    {
        var game = DetectGame(_master);
        if (game == null || game == CurrentGame)
            return;
        var effective = _master.ForGame(game);
        CurrentGame = game;
        if (effective.Mode != _settings.Mode)
        {
            Start(_master); // autre mode pour ce jeu (ex. LMU en capture, AC en caméra)
            return;
        }
        ApplyEffective(EffectiveFor(game));
    }

    void ApplyEffective(Settings effective)
    {
        // Le mode demande un redémarrage : il reste celui du démarrage.
        effective.Mode = _settings.Mode;
        var copy = effective;
        _settings = copy;
        _capture?.SetCrop(new Rectangle(copy.CropX, copy.CropY, copy.CropWidth, copy.CropHeight));
        _usb?.UpdateSettings(copy);
        _radar?.UpdateSettings(copy);
        _acCamera?.UpdateSettings(copy);
        _spotter?.UpdateSettings(copy);
        _hud?.UpdateSettings(copy);
    }

    public void Stop()
    {
        _watchdog.Stop();
        _standbyTimer.Stop();
        Frames.Held = false;
        _activity?.Dispose();
        _activity = null;
        StopCapture();
        Frames.PostProcess = null;
        Frames.UpscaleTo = null;
        _hudPostProcess = null;
        _upscale = null;
        _calibrating = false;
        _hud?.Dispose();
        _hud = null;
        _spotter?.Dispose();
        _spotter = null;
        _radar?.Dispose();
        _radar = null;
        _acCamera?.Dispose();
        _acCamera = null;
        _usb?.Dispose();
        _usb = null;
        _mask?.Close();
        _mask?.Dispose();
        _mask = null;
        Frames.Clear();
        Running = false;
        Status = "Arrêté.";
    }

    void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
        _captureWindow = IntPtr.Zero;
        _captureClosed = false;
    }

    void Tick()
    {
        if (!Running)
            return;

        FollowGame();
        if (!Running)
            return;
        var s = _settings;
        var messages = new List<string>();
        if (CurrentGame is { } current)
            messages.Add($"Profil : {Games.Get(current).Name}" + (_master.OverrideCount(current) > 0 ? "" : " (réglages communs)"));

        UpdateState(s);
        messages.Add(_state switch
        {
            GameState.Paused => "Jeu en pause ou dans les menus : écran de veille AVIX.",
            GameState.Desktop => "Jeu en arrière-plan : logo AVIX sur le VoCore.",
            GameState.NoGame => "En attente du jeu : écran de veille AVIX.",
            _ => "",
        });

        // Seul le mode Capture a besoin de la fenêtre du jeu ; Radar et Caméra AC lisent la télémétrie.
        if (s.Mode == MirrorMode.Capture)
        {
            // Fenêtre du jeu choisi (LMU par défaut ; autres jeux en beta, en mode Auto le premier lancé).
            _gameWindow = IntPtr.Zero;
            foreach (var game in Games.Active(s))
            {
                foreach (var process in Games.ProcessesOf(game, s))
                {
                    _gameWindow = GameWindow.Find(process);
                    if (_gameWindow != IntPtr.Zero)
                        break;
                }
                if (_gameWindow != IntPtr.Zero)
                    break;
            }
            if (_gameWindow == IntPtr.Zero)
            {
                messages.Add(s.RadarGame == RadarGame.Auto ? "En attente du jeu…" : $"En attente de {Games.Get(s.RadarGame).Name}…");
                _gameSeenWindow = IntPtr.Zero;
            }
            else
            {
                // On ne touche pas au jeu tant qu'Easy Anti-Cheat n'a pas fini de démarrer.
                if (_gameSeenWindow != _gameWindow)
                {
                    _gameSeenWindow = _gameWindow;
                    _gameSeen.Restart();
                }
                double wait = s.GameStartDelaySeconds - _gameSeen.Elapsed.TotalSeconds;
                if (wait > 0)
                {
                    messages.Add($"Jeu détecté — démarrage dans {Math.Ceiling(wait)} s (laisse l'anti-triche finir).");
                    _gameWindow = IntPtr.Zero;
                }
            }
        }

        if (s.Mode == MirrorMode.Capture)
        {
            messages.Add(TickCapture(s));
            UpdateMask(s);
        }
        else if (s.Mode == MirrorMode.Radar && _radar != null)
        {
            messages.Add(_radar.Status);
        }
        else if (_acCamera != null)
        {
            messages.Add(_acCamera.Status);
        }

        if (_usb != null)
            messages.Add(_usb.Status);

        Status = string.Join("\n", messages.Where(m => !string.IsNullOrEmpty(m)));
    }

    string TickCapture(Settings s)
    {
        if (!WgcCapture.IsSupported)
            return "Capture Windows.Graphics.Capture non supportée (Windows 10 2004 ou plus récent requis).";

        if (_captureClosed)
            StopCapture();

        if (_gameWindow == IntPtr.Zero)
        {
            StopCapture();
            return "";
        }
        if (_capture != null && _captureWindow != _gameWindow)
            StopCapture();
        if (_capture == null)
        {
            try
            {
                _capture = WgcCapture.ForWindow(_gameWindow, Frames);
                _captureWindow = _gameWindow;
                ConfigureAndStart(s);
            }
            catch (Exception ex)
            {
                StopCapture();
                return "Erreur de capture : " + ex.Message;
            }
        }

        if (_capture == null)
            return "";
        var src = _capture.SourceSize;
        var zone = s.CropWidth > 0 && s.CropHeight > 0
            ? $"zone {s.CropWidth}x{s.CropHeight} @ {s.CropX},{s.CropY}"
            : "image entière (utilisez « Calibrer la zone »)";
        return $"Capture {src.Width}x{src.Height} — {zone}";
    }

    /// <summary>Pose le cache sur la zone du rétro (plus ses marges), sur l'écran principal.</summary>
    void UpdateMask(Settings s)
    {
        bool show = s.HideMirrorOnScreen && _state == GameState.Active && _capture != null && _captureWindow != IntPtr.Zero && Native.IsWindow(_captureWindow) &&
                    !Native.IsIconic(_captureWindow) && s.CropWidth > 0 && s.CropHeight > 0;
        if (!show)
        {
            _mask?.Hide();
            return;
        }

        // La capture de fenêtre commence au coin visible de la fenêtre du jeu.
        var window = Native.GetVisibleBounds(_captureWindow);
        _mask ??= new MaskForm();
        _mask.MatchColor = s.MaskMatchColor;
        _mask.Samples = s.MaskSamples;
        if (s.MaskShape is { Count: >= 3 } shape)
        {
            // Forme libre (rétro F1…) : le cache entoure le contour tracé, sans marges.
            var zone = new RectangleF(window.X + s.CropX, window.Y + s.CropY, s.CropWidth, s.CropHeight);
            using var path = MaskForm.ShapePath(shape, zone, s.MaskShapeSmooth);
            var shapeBounds = Rectangle.Ceiling(path.GetBounds());
            var outline = shape.Select(p => new PointF(
                zone.X + (float)(p.X / 100 * zone.Width) - shapeBounds.X,
                zone.Y + (float)(p.Y / 100 * zone.Height) - shapeBounds.Y)).ToArray();
            _mask.Cover(shapeBounds, outline, s.MaskShapeSmooth);
            return;
        }
        int left = Math.Max(0, s.MaskMarginLeft), right = Math.Max(0, s.MaskMarginRight);
        int top = Math.Max(0, s.MaskMarginTop), bottom = Math.Max(0, s.MaskMarginBottom);
        var bounds = new Rectangle(window.X + s.CropX - left, window.Y + s.CropY - top,
            s.CropWidth + left + right, s.CropHeight + top + bottom);
        _mask.Cover(bounds);
    }

    void ConfigureAndStart(Settings s)
    {
        var capture = _capture!;
        capture.SetCrop(new Rectangle(s.CropX, s.CropY, s.CropWidth, s.CropHeight));
        capture.SetFps(s.TargetFps);
        capture.Closed += () => _captureClosed = true;
        capture.Start();
    }

    /// <summary>
    /// Au volant : image du jeu. Pause ou jeu absent : animation AVIX. Bureau : logo AVIX fixe.
    /// Hors du volant, le cache sur l'écran principal est retiré et les LEDs sont éteintes.
    /// </summary>
    void UpdateState(Settings s)
    {
        var state = _calibrating ? GameState.Active : _activity?.Update(s) ?? GameState.NoGame;
        bool changed = state != _state || !_stateShown;
        _state = state;
        _stateShown = true;

        bool standby = state != GameState.Active;
        Frames.Held = standby;
        if (_spotter != null)
            _spotter.Suspended = standby;
        if (!changed)
            return;

        if (state is GameState.Paused or GameState.NoGame)
        {
            _standbyTimer.Start();
            DrawStandby();
        }
        else
        {
            _standbyTimer.Stop();
            if (state == GameState.Desktop)
                DrawStandby();
        }
    }

    void DrawStandby()
    {
        if (!Running || _usb == null)
            return;
        var size = _usb.LogicalSize;
        double t = _standbyClock.Elapsed.TotalSeconds;

        // Image ou animation personnalisée (avec le petit logo AVIX), sinon l'écran AVIX.
        var image = _settings.StandbyImage;
        if (!string.IsNullOrWhiteSpace(image))
        {
            bool drawn = false;
            double time = _state == GameState.Desktop ? 0 : t;
            Frames.WriteStandby(size.Width, size.Height, bmp => drawn = Ui.Splash.DrawCustom(bmp, image, _settings.StandbyImageFill, time));
            if (drawn)
                return;
        }

        switch (_state)
        {
            case GameState.Desktop:
                Frames.WriteStandby(size.Width, size.Height, bmp => Ui.Splash.Draw(bmp, ""));
                break;
            case GameState.Paused:
            case GameState.NoGame:
                string message = _state == GameState.Paused ? "Pause" : "En attente du jeu…";
                Frames.WriteStandby(size.Width, size.Height, bmp => Ui.Splash.DrawAnimated(bmp, t, message));
                break;
        }
    }

    public void Dispose()
    {
        Stop();
        _standbyTimer.Dispose();
        _watchdog.Dispose();
        Frames.Dispose();
    }
}
