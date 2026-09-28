using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Cache posé sur le rétro virtuel de LMU, sur l'écran principal.
/// Il laisse passer les clics et n'apparaît pas dans la capture (qui lit la fenêtre du jeu).
/// Il reprend la couleur du décor lue en un ou plusieurs points autour de lui : avec plusieurs
/// points, il se remplit d'un dégradé entre leurs couleurs pour se fondre dans l'image.
/// </summary>
public sealed class MaskForm : Form
{
    const double Smoothing = 0.35;  // part de la nouvelle couleur à chaque lecture (évite le scintillement)
    const int GridMax = 32;         // résolution du dégradé (agrandi en douceur à la taille du cache)

    readonly System.Windows.Forms.Timer _sampler = new() { Interval = 100 };
    readonly Bitmap _pixel = new(1, 1, PixelFormat.Format32bppRgb);
    readonly List<(double R, double G, double B)> _colors = new();
    Bitmap? _gradient;

    /// <summary>Vrai : couleur du décor autour du cache ; faux : noir.</summary>
    public bool MatchColor { get; set; } = true;

    /// <summary>Points lus autour du cache.</summary>
    public IReadOnlyList<MaskSample> Samples { get; set; } = new[] { new MaskSample() };

    public MaskForm()
    {
        Text = "AviX Mirror — cache";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _sampler.Tick += (_, _) => SampleColors();
        _sampler.Start();
    }

    /// <summary>Position d'un point de couleur, en pixels écran, autour d'un cache de bornes <paramref name="bounds"/>.</summary>
    public static Point ScreenPoint(MaskSample sample, Rectangle bounds)
    {
        double t = Math.Clamp(sample.Position, 0, 100) / 100;
        int d = Math.Max(1, sample.Distance);
        return sample.Side switch
        {
            MaskSide.Bas => new Point(bounds.Left + (int)(t * (bounds.Width - 1)), bounds.Bottom - 1 + d),
            MaskSide.Gauche => new Point(bounds.Left - d, bounds.Top + (int)(t * (bounds.Height - 1))),
            MaskSide.Droite => new Point(bounds.Right - 1 + d, bounds.Top + (int)(t * (bounds.Height - 1))),
            _ => new Point(bounds.Left + (int)(t * (bounds.Width - 1)), bounds.Top - d),
        };
    }

    /// <summary>Position du point sur le bord du cache, en coordonnées normalisées (0..1).</summary>
    static PointF EdgePoint(MaskSample sample)
    {
        float t = (float)(Math.Clamp(sample.Position, 0, 100) / 100);
        return sample.Side switch
        {
            MaskSide.Bas => new PointF(t, 1),
            MaskSide.Gauche => new PointF(0, t),
            MaskSide.Droite => new PointF(1, t),
            _ => new PointF(t, 0),
        };
    }

    /// <summary>Lit la couleur de l'écran à chaque point et l'applique en douceur.</summary>
    void SampleColors()
    {
        if (!Visible)
            return;
        var samples = Samples;
        if (!MatchColor || samples.Count == 0)
        {
            _colors.Clear();
            SetGradient(null);
            if (BackColor != Color.Black)
                BackColor = Color.Black;
            return;
        }

        var bounds = Bounds;
        var screen = Screen.FromRectangle(bounds).Bounds;
        if (_colors.Count != samples.Count)
            _colors.Clear();
        for (int i = 0; i < samples.Count; i++)
        {
            var p = ScreenPoint(samples[i], bounds);
            p = new Point(Math.Clamp(p.X, screen.Left, screen.Right - 1), Math.Clamp(p.Y, screen.Top, screen.Bottom - 1));
            Color c;
            try
            {
                using (var g = Graphics.FromImage(_pixel))
                    g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(1, 1));
                c = _pixel.GetPixel(0, 0);
            }
            catch
            {
                return; // écran verrouillé, bureau sécurisé…
            }
            if (_colors.Count <= i)
                _colors.Add((c.R, c.G, c.B));
            else
            {
                var old = _colors[i];
                _colors[i] = (old.R + (c.R - old.R) * Smoothing, old.G + (c.G - old.G) * Smoothing, old.B + (c.B - old.B) * Smoothing);
            }
        }

        if (samples.Count == 1)
        {
            SetGradient(null);
            var (cr, cg, cb) = _colors[0];
            var color = Color.FromArgb((int)Math.Round(cr), (int)Math.Round(cg), (int)Math.Round(cb));
            if (BackColor != color)
                BackColor = color;
            return;
        }
        SetGradient(BuildGradient(samples));
    }

    /// <summary>
    /// Dégradé entre les points : chaque pixel prend la moyenne des couleurs pondérée par l'inverse du
    /// carré de la distance à chaque point (sur une petite grille, agrandie ensuite en douceur).
    /// </summary>
    Bitmap BuildGradient(IReadOnlyList<MaskSample> samples)
    {
        var size = ClientSize;
        double aspect = size.Height > 0 ? size.Width / (double)size.Height : 1;
        int gw = aspect >= 1 ? GridMax : Math.Max(2, (int)(GridMax * aspect));
        int gh = aspect >= 1 ? Math.Max(2, (int)(GridMax / aspect)) : GridMax;
        var points = samples.Select(EdgePoint).ToArray();

        var bmp = new Bitmap(gw, gh, PixelFormat.Format32bppRgb);
        for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
        {
            // Distances mesurées en pixels réels, pour que le dégradé suive la forme du cache.
            double px = (x + 0.5) / gw, py = (y + 0.5) / gh;
            double r = 0, g = 0, b = 0, sum = 0;
            for (int i = 0; i < points.Length; i++)
            {
                double dx = (px - points[i].X) * aspect, dy = py - points[i].Y;
                double w = 1 / (dx * dx + dy * dy + 1e-4);
                r += _colors[i].R * w; g += _colors[i].G * w; b += _colors[i].B * w;
                sum += w;
            }
            bmp.SetPixel(x, y, Color.FromArgb((int)(r / sum), (int)(g / sum), (int)(b / sum)));
        }
        return bmp;
    }

    void SetGradient(Bitmap? gradient)
    {
        var old = _gradient;
        _gradient = gradient;
        old?.Dispose();
        if (gradient != null || old != null)
            Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_gradient == null)
        {
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(brush, ClientRectangle);
            return;
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // pas de bord sombre
        e.Graphics.DrawImage(_gradient, ClientRectangle, 0, 0, _gradient.Width, _gradient.Height, GraphicsUnit.Pixel, attributes);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Tout est dessiné dans OnPaint (évite le scintillement).
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sampler.Dispose();
            _pixel.Dispose();
            _gradient?.Dispose();
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
