using AviXMirror.Output;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>Fenêtre plein écran affichée sur le VoCore.</summary>
public sealed class MirrorForm : Form
{
    readonly FrameBuffer _frames;
    readonly Func<string> _status;
    Settings _settings;
    int _invalidatePending;

    public MirrorForm(FrameBuffer frames, Settings settings, Func<string> status)
    {
        _frames = frames;
        _settings = settings;
        _status = status;

        Text = "AviX Mirror";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        PlaceOnScreen();
        _frames.Updated += OnFrameUpdated;
    }

    public Screen TargetScreen => ScreenHelper.Find(_settings.OutputScreen) ?? ScreenHelper.DefaultOutput();

    public void UpdateSettings(Settings settings)
    {
        _settings = settings;
        PlaceOnScreen();
        Invalidate();
    }

    void PlaceOnScreen() => Bounds = TargetScreen.Bounds;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Ne vole pas le focus au jeu et n'apparaît pas dans Alt+Tab.
            cp.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Évite que le rétro se capture lui-même si la source est un écran.
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    void OnFrameUpdated()
    {
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 1 || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(Invalidate);
        }
        catch (InvalidOperationException)
        {
            // Fenêtre en cours de fermeture.
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Tout est dessiné dans OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Interlocked.Exchange(ref _invalidatePending, 0);
        var g = e.Graphics;
        var s = _settings;
        var client = ClientRectangle;

        bool drawn = _frames.Read(bmp =>
            FrameRenderer.Draw(g, bmp, client.Size, (int)s.Rotation, s.FlipHorizontal, s.Stretch));

        if (!drawn)
        {
            g.Clear(Color.Black);
            using var font = new Font("Segoe UI", Math.Clamp(client.Height / 16f, 10, 28), FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "AviX Mirror\n" + _status(), font, client, Color.Silver,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _frames.Updated -= OnFrameUpdated;
        base.OnFormClosed(e);
    }
}
