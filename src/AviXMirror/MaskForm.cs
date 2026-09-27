using System.Drawing.Imaging;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Cache posé sur le rétro virtuel de LMU, sur l'écran principal.
/// Il laisse passer les clics et n'apparaît pas dans la capture (qui lit la fenêtre du jeu).
/// Il peut prendre la couleur de l'image juste sous son bord inférieur pour se fondre dans le décor.
/// </summary>
public sealed class MaskForm : Form
{
    const double Smoothing = 0.35;  // part de la nouvelle couleur à chaque lecture (évite le scintillement)

    readonly System.Windows.Forms.Timer _sampler = new() { Interval = 100 };
    double _r, _g, _b;
    bool _hasColor;
    Bitmap? _row;

    /// <summary>Vrai : couleur du décor sous le cache ; faux : noir.</summary>
    public bool MatchColor { get; set; } = true;

    public MaskForm()
    {
        Text = "AviX Mirror — cache";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        _sampler.Tick += (_, _) => SampleColor();
        _sampler.Start();
    }

    /// <summary>Lit la couleur moyenne de l'écran 1 px sous le cache et l'applique en douceur.</summary>
    void SampleColor()
    {
        if (!Visible)
            return;
        if (!MatchColor)
        {
            _hasColor = false;
            if (BackColor != Color.Black)
                BackColor = Color.Black;
            return;
        }

        var b = Bounds;
        var screen = Screen.FromRectangle(b).Bounds;
        int y = Math.Clamp(b.Bottom, screen.Top, screen.Bottom - 1); // première ligne sous le cache
        int x0 = Math.Clamp(b.Left, screen.Left, screen.Right - 1);
        int x1 = Math.Clamp(b.Right, x0 + 1, screen.Right);
        int width = x1 - x0;

        // Une seule lecture de l'écran : la ligne sous le cache, moyennée.
        if (_row == null || _row.Width != width)
        {
            _row?.Dispose();
            _row = new Bitmap(width, 1, PixelFormat.Format32bppRgb);
        }
        try
        {
            using var g = Graphics.FromImage(_row);
            g.CopyFromScreen(x0, y, 0, 0, new Size(width, 1));
        }
        catch
        {
            return; // écran verrouillé, bureau sécurisé…
        }
        double r = 0, gr = 0, bl = 0;
        var data = _row.LockBits(new Rectangle(0, 0, width, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            unsafe
            {
                uint* p = (uint*)data.Scan0;
                for (int i = 0; i < width; i++)
                {
                    uint c = p[i];
                    r += (c >> 16) & 0xFF; gr += (c >> 8) & 0xFF; bl += c & 0xFF;
                }
            }
        }
        finally
        {
            _row.UnlockBits(data);
        }
        r /= width; gr /= width; bl /= width;

        double k = _hasColor ? Smoothing : 1;
        _r += (r - _r) * k; _g += (gr - _g) * k; _b += (bl - _b) * k;
        _hasColor = true;
        var color = Color.FromArgb((int)Math.Round(_r), (int)Math.Round(_g), (int)Math.Round(_b));
        if (BackColor != color)
            BackColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sampler.Dispose();
            _row?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW |
                                Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
    }

    /// <summary>Place le cache sur <paramref name="bounds"/> (coordonnées écran) et le garde au premier plan.</summary>
    public void Cover(Rectangle bounds)
    {
        if (Bounds != bounds)
            Bounds = bounds;
        if (!Visible)
            Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
