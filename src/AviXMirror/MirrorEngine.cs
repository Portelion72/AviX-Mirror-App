using AviXMirror.Capture;
using AviXMirror.Radar;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Orchestration : recherche de LMU, extension de sa fenêtre, capture ou radar,
/// affichage sur le VoCore et flux MJPEG. Toutes les méthodes publiques s'appellent sur le thread UI.
/// </summary>
public sealed class MirrorEngine : IDisposable
{
    readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 1000 };

    Settings _settings = new();
    WgcCapture? _capture;
    IntPtr _captureWindow;
    volatile bool _captureClosed;
    RadarSource? _radar;
    MirrorForm? _form;
    MjpegServer? _mjpeg;

    IntPtr _gameWindow;
    OriginalWindowState? _original;

    record OriginalWindowState(IntPtr Hwnd, IntPtr Style, IntPtr ExStyle, Rectangle Rect);

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

        if (_settings.Mode != MirrorMode.Direct)
        {
            _form = new MirrorForm(Frames, _settings, () => Status);
            _form.Show();
        }

        if (_settings.Mode == MirrorMode.Radar)
        {
            _radar = new RadarSource(Frames, _settings);
            if (_form != null)
                _radar.FallbackSize = _form.Size;
        }

        if (_settings.MjpegPort > 0 && _settings.Mode != MirrorMode.Direct)
        {
            try
            {
                _mjpeg = new MjpegServer(Frames, _settings.MjpegPort, Math.Min(_settings.TargetFps, 30));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir le port {_settings.MjpegPort} : {ex.Message}", "AviX Mirror");
            }
        }

        Tick();
        _watchdog.Start();
    }

    public void Stop()
    {
        _watchdog.Stop();
        StopCapture();
        _radar?.Dispose();
        _radar = null;
        _mjpeg?.Dispose();
        _mjpeg = null;
        _form?.Close();
        _form?.Dispose();
        _form = null;
        RestoreGameWindow();
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

        bool needGame = s.Mode == MirrorMode.Direct || s.ExtendGameWindow ||
                        (s.Mode == MirrorMode.Capture && s.Source == CaptureSource.FenetreJeu);
        if (needGame)
        {
            _gameWindow = GameWindow.Find(s.GameProcessName);
            if (_gameWindow == IntPtr.Zero)
                messages.Add("En attente de Le Mans Ultimate…");
        }

        if (_gameWindow != IntPtr.Zero && (s.Mode == MirrorMode.Direct || s.ExtendGameWindow))
        {
            RememberGameWindow(_gameWindow);
            GameWindow.Extend(_gameWindow, s);
            var r = GameWindow.GetRect(_gameWindow);
            messages.Add($"Fenêtre LMU : {r.Width}x{r.Height} @ {r.X},{r.Y}");
        }

        if (s.Mode == MirrorMode.Capture)
            messages.Add(TickCapture(s));
        else if (s.Mode == MirrorMode.Radar && _radar != null)
            messages.Add(_radar.Status);
        else if (s.Mode == MirrorMode.Direct && _gameWindow != IntPtr.Zero)
            messages.Add("Mode direct : placez le rétro virtuel de LMU dans la bande étendue.");

        if (_mjpeg != null)
            messages.Add($"Flux : http://localhost:{s.MjpegPort}/");

        Status = string.Join("\n", messages.Where(m => !string.IsNullOrEmpty(m)));
    }

    string TickCapture(Settings s)
    {
        if (!WgcCapture.IsSupported)
            return "Capture Windows.Graphics.Capture non supportée (Windows 10 2004 ou plus récent requis).";

        if (_captureClosed)
            StopCapture();

        if (s.Source == CaptureSource.FenetreJeu)
        {
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
        }
        else if (_capture == null)
        {
            var screen = ScreenHelper.Find(s.SourceScreen) ?? Screen.PrimaryScreen!;
            var center = new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
            try
            {
                _capture = WgcCapture.ForMonitor(Native.MonitorFromPoint(center, Native.MONITOR_DEFAULTTONEAREST), Frames);
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

    void ConfigureAndStart(Settings s)
    {
        var capture = _capture!;
        capture.SetCrop(new Rectangle(s.CropX, s.CropY, s.CropWidth, s.CropHeight));
        capture.SetFps(s.TargetFps);
        capture.SetCursor(s.CaptureCursor);
        capture.Closed += () => _captureClosed = true;
        capture.Start();
    }

    void RememberGameWindow(IntPtr hwnd)
    {
        if (_original?.Hwnd == hwnd)
            return;
        _original = new OriginalWindowState(hwnd,
            Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE),
            Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE),
            GameWindow.GetRect(hwnd));
    }

    void RestoreGameWindow()
    {
        var o = _original;
        _original = null;
        if (o == null || !Native.IsWindow(o.Hwnd))
            return;
        Native.SetWindowLongPtr(o.Hwnd, Native.GWL_STYLE, o.Style);
        Native.SetWindowLongPtr(o.Hwnd, Native.GWL_EXSTYLE, o.ExStyle);
        Native.SetWindowPos(o.Hwnd, IntPtr.Zero, o.Rect.X, o.Rect.Y, o.Rect.Width, o.Rect.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_FRAMECHANGED);
    }

    public void Dispose()
    {
        Stop();
        _watchdog.Dispose();
        Frames.Dispose();
    }
}
