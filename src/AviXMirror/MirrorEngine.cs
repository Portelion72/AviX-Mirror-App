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
    readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 1000 };

    Settings _settings = new();
    WgcCapture? _capture;
    IntPtr _captureWindow;
    volatile bool _captureClosed;
    RadarSource? _radar;
    MaskForm? _mask;
    VoCoreUsbOutput? _usb;
    AcCameraSource? _acCamera;
    Spotter? _spotter;
    Hud.HudOverlay? _hud;

    /// <summary>Couleurs des LEDs du spotter (gauche, droite) pour l'aperçu, ou vides.</summary>
    public (Color[] Left, Color[] Right) LedSides => _spotter?.Sides ?? (Array.Empty<Color>(), Array.Empty<Color>());

    IntPtr _gameWindow;
    IntPtr _gameSeenWindow;
    readonly Stopwatch _gameSeen = new();

    public FrameBuffer Frames { get; } = new();
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Arrêté.";
    public WgcCapture? Capture => _capture;

    public MirrorEngine()
    {
        _watchdog.Tick += (_, _) => Tick();
    }

    public void Start(Settings settings)
    {
        Stop();
        _settings = settings.Clone();
        Running = true;

        _usb = new VoCoreUsbOutput(Frames, _settings);

        // Écran d'accueil AVIX_3D sur le VoCore en attendant les premières images.
        var size = _usb.LogicalSize;
        Frames.Write(size.Width, size.Height, bmp => Ui.Splash.Draw(bmp, "En attente du jeu…"));

        // ATH par-dessus les images des modes caméra et capture (le radar dessine le sien).
        if (_settings.Mode is MirrorMode.CameraAssettoCorsa or MirrorMode.Capture)
        {
            _hud = new Hud.HudOverlay(_settings, _settings.Mode);
            var hud = _hud;
            Frames.PostProcess = bmp => hud.Draw(bmp);
            // Capture LMU : ATH dessiné en pleine résolution de l'écran, par-dessus l'image agrandie.
            if (_settings.Mode == MirrorMode.Capture)
                Frames.UpscaleTo = size;
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
        var copy = settings.Clone();
        // Les réglages de structure (mode, jeu, sortie) demandent un redémarrage : on les garde.
        copy.Mode = _settings.Mode;
        _settings = copy;
        _usb?.UpdateSettings(copy);
        _radar?.UpdateSettings(copy);
        _acCamera?.UpdateSettings(copy);
        _spotter?.UpdateSettings(copy);
        _hud?.UpdateSettings(copy);
    }

    public void Stop()
    {
        _watchdog.Stop();
        StopCapture();
        Frames.PostProcess = null;
        Frames.UpscaleTo = null;
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

        var s = _settings;
        var messages = new List<string>();

        // Seul le mode Capture a besoin de la fenêtre du jeu ; Radar et Caméra AC lisent la télémétrie.
        if (s.Mode == MirrorMode.Capture)
        {
            _gameWindow = GameWindow.Find(s.GameProcessName);
            if (_gameWindow == IntPtr.Zero)
            {
                messages.Add("En attente de Le Mans Ultimate…");
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
                    messages.Add($"LMU détecté — démarrage dans {Math.Ceiling(wait)} s (laisse Easy Anti-Cheat finir).");
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
        bool show = s.HideMirrorOnScreen && _capture != null && _captureWindow != IntPtr.Zero && Native.IsWindow(_captureWindow) &&
                    !Native.IsIconic(_captureWindow) && s.CropWidth > 0 && s.CropHeight > 0;
        if (!show)
        {
            _mask?.Hide();
            return;
        }

        // La capture de fenêtre commence au coin visible de la fenêtre du jeu.
        var window = Native.GetVisibleBounds(_captureWindow);
        int left = Math.Max(0, s.MaskMarginLeft), right = Math.Max(0, s.MaskMarginRight);
        int top = Math.Max(0, s.MaskMarginTop), bottom = Math.Max(0, s.MaskMarginBottom);
        var bounds = new Rectangle(window.X + s.CropX - left, window.Y + s.CropY - top,
            s.CropWidth + left + right, s.CropHeight + top + bottom);
        _mask ??= new MaskForm();
        _mask.MatchColor = s.MaskMatchColor;
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

    public void Dispose()
    {
        Stop();
        _watchdog.Dispose();
        Frames.Dispose();
    }
}
