using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Cache posé sur le rétro virtuel de LMU, sur l'écran principal.
/// Il laisse passer les clics et n'apparaît pas dans la capture (qui lit la fenêtre du jeu).
/// Il reprend la couleur du décor lue en un ou plusieurs points autour de lui : avec plusieurs
/// points, il se remplit d'un dégradé entre leurs couleurs pour se fondre dans l'image.
/// Sa forme est un rectangle ou un contour libre qui reprend celle du rétro du jeu (F1…).
/// </summary>
public sealed class MaskForm : Form
{
    const double Smoothing = 0.35;  // part de la nouvelle couleur à chaque lecture (évite le scintillement)
    const int GridMax = 32;         // résolution du dégradé (agrandi en douceur à la taille du cache)

    readonly System.Windows.Forms.Timer _sampler = new() { Interval = 100 };
    readonly Bitmap _pixel = new(1, 1, PixelFormat.Format32bppRgb);
    readonly List<(double R, double G, double B)> _colors = new();
    Bitmap? _gradient;
    PointF[]? _shape;
    bool _shapeSmooth;

    /// <summary>Vrai : couleur du décor autour du cache ; faux : noir.</summary>
    public bool MatchColor { get; set; } = true;

    /// <summary>
    /// Vrai : le cache prolonge l'image lue sur tout son contour (bords haut, bas, gauche et droite), comme un
    /// flou qui raccorde le décor ; faux : dégradé entre les points de couleur.
    /// </summary>
    public bool BlendEdges { get; set; }

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

    /// <summary>Forme libre du cache : contour en coordonnées de <paramref name="zone"/> (la zone de capture).</summary>
    public static GraphicsPath ShapePath(IReadOnlyList<MaskPoint> shape, RectangleF zone, bool smooth)
    {
        var points = shape.Select(p => new PointF(zone.X + (float)(p.X / 100 * zone.Width), zone.Y + (float)(p.Y / 100 * zone.Height))).ToArray();
        var path = new GraphicsPath();
        if (points.Length < 3)
            path.AddRectangle(zone);
        else if (smooth)
            path.AddClosedCurve(points, 0.5f);
        else
            path.AddPolygon(points);
        return path;
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
        if (BlendEdges)
        {
            SampleContour(bounds, screen);
            return;
        }
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

    // ---------- Fondu sur tout le contour ----------

    const int EdgeSamples = 40;     // couleurs lues le long de chaque bord
    const int EdgeDistance = 3;     // distance au bord du cache (px), hors du liseré du rétro du jeu
    double[,]? _top, _bottom, _left, _right; // [échantillon, canal], lissés dans le temps
    Bitmap? _rowStrip, _columnStrip;

    /// <summary>
    /// Lit l'écran sur une ligne juste au-dessus, une juste en dessous, une colonne à gauche et une à droite du
    /// cache, puis remplit le cache par interpolation de Coons entre ces quatre bords : le décor se prolonge
    /// (pilier de cabine, ciel, tableau de bord…) au lieu d'un aplat.
    /// </summary>
    void SampleContour(Rectangle bounds, Rectangle screen)
    {
        try
        {
            var top = ReadEdge(bounds.Left, bounds.Top - EdgeDistance, bounds.Width, horizontal: true, screen);
            var bottom = ReadEdge(bounds.Left, bounds.Bottom - 1 + EdgeDistance, bounds.Width, horizontal: true, screen);
            var left = ReadEdge(bounds.Left - EdgeDistance, bounds.Top, bounds.Height, horizontal: false, screen);
            var right = ReadEdge(bounds.Right - 1 + EdgeDistance, bounds.Top, bounds.Height, horizontal: false, screen);
            _top = Smooth(_top, top);
            _bottom = Smooth(_bottom, bottom);
            _left = Smooth(_left, left);
            _right = Smooth(_right, right);
        }
        catch
        {
            return; // écran verrouillé, bureau sécurisé…
        }
        SetGradient(BuildCoons(_top!, _bottom!, _left!, _right!));
    }

    static double[,] Smooth(double[,]? old, double[,] fresh)
    {
        if (old == null || old.GetLength(0) != fresh.GetLength(0))
            return fresh;
        for (int i = 0; i < fresh.GetLength(0); i++)
            for (int c = 0; c < 3; c++)
                old[i, c] += (fresh[i, c] - old[i, c]) * Smoothing;
        return old;
    }

    /// <summary>Couleurs moyennes le long d'un bord (ligne ou colonne d'écran), en <see cref="EdgeSamples"/> morceaux.</summary>
    unsafe double[,] ReadEdge(int x, int y, int length, bool horizontal, Rectangle screen)
    {
        length = Math.Max(1, length);
        x = Math.Clamp(x, screen.Left, screen.Right - 1);
        y = Math.Clamp(y, screen.Top, screen.Bottom - 1);
        if (horizontal)
            length = Math.Min(length, screen.Right - x);
        else
            length = Math.Min(length, screen.Bottom - y);
        length = Math.Max(1, length);

        ref Bitmap? strip = ref horizontal ? ref _rowStrip : ref _columnStrip;
        var size = horizontal ? new Size(length, 1) : new Size(1, length);
        if (strip == null || strip.Size != size)
        {
            strip?.Dispose();
            strip = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        }
        using (var g = Graphics.FromImage(strip))
            g.CopyFromScreen(x, y, 0, 0, size);

        var result = new double[EdgeSamples, 3];
        var data = strip.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int i = 0; i < EdgeSamples; i++)
            {
                int from = i * length / EdgeSamples, to = Math.Max(from + 1, (i + 1) * length / EdgeSamples);
                double r = 0, gg = 0, b = 0;
                for (int k = from; k < to && k < length; k++)
                {
                    uint pixel = horizontal
                        ? ((uint*)data.Scan0)[k]
                        : *(uint*)((byte*)data.Scan0 + (long)k * data.Stride);
                    r += (pixel >> 16) & 0xFF;
                    gg += (pixel >> 8) & 0xFF;
                    b += pixel & 0xFF;
                }
                int n = Math.Max(1, Math.Min(to, length) - from);
                result[i, 0] = r / n;
                result[i, 1] = gg / n;
                result[i, 2] = b / n;
            }
        }
        finally
        {
            strip.UnlockBits(data);
        }
        return result;
    }

    /// <summary>Surface de Coons : chaque point mélange les quatre bords selon sa position (raccord exact sur les bords).</summary>
    Bitmap BuildCoons(double[,] top, double[,] bottom, double[,] left, double[,] right)
    {
        var size = ClientSize;
        double aspect = size.Height > 0 ? size.Width / (double)size.Height : 1;
        int gw = aspect >= 1 ? 48 : Math.Max(4, (int)(48 * aspect));
        int gh = aspect >= 1 ? Math.Max(4, (int)(48 / aspect)) : 48;
        int last = EdgeSamples - 1;

        static double At(double[,] edge, double t, int c)
        {
            double f = Math.Clamp(t, 0, 1) * (edge.GetLength(0) - 1);
            int i = (int)f;
            int j = Math.Min(i + 1, edge.GetLength(0) - 1);
            return edge[i, c] + (edge[j, c] - edge[i, c]) * (f - i);
        }

        var bmp = new Bitmap(gw, gh, PixelFormat.Format32bppRgb);
        for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
        {
            double u = (x + 0.5) / gw, v = (y + 0.5) / gh;
            var rgb = new int[3];
            for (int c = 0; c < 3; c++)
            {
                double corners = (1 - u) * (1 - v) * (top[0, c] + left[0, c]) / 2 + u * (1 - v) * (top[last, c] + right[0, c]) / 2 +
                                 (1 - u) * v * (bottom[0, c] + left[last, c]) / 2 + u * v * (bottom[last, c] + right[last, c]) / 2;
                double value = (1 - v) * At(top, u, c) + v * At(bottom, u, c) + (1 - u) * At(left, v, c) + u * At(right, v, c) - corners;
                rgb[c] = (int)Math.Clamp(Math.Round(value), 0, 255);
            }
            bmp.SetPixel(x, y, Color.FromArgb(rgb[0], rgb[1], rgb[2]));
        }
        return bmp;
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
            _rowStrip?.Dispose();
            _columnStrip?.Dispose();
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

    /// <summary>
    /// Place le cache sur <paramref name="bounds"/> (coordonnées écran) et le garde au premier plan.
    /// <paramref name="shape"/> : contour libre relatif à <paramref name="bounds"/>, ou null pour un rectangle.
    /// </summary>
    public void Cover(Rectangle bounds, PointF[]? shape = null, bool smooth = false)
    {
        if (Bounds != bounds)
            Bounds = bounds;
        bool same = smooth == _shapeSmooth && (shape == null ? _shape == null : _shape != null && shape.SequenceEqual(_shape));
        if (!same)
        {
            _shape = shape;
            _shapeSmooth = smooth;
            var old = Region;
            if (shape == null || shape.Length < 3)
            {
                Region = null;
            }
            else
            {
                using var path = new GraphicsPath();
                if (smooth)
                    path.AddClosedCurve(shape, 0.5f);
                else
                    path.AddPolygon(shape);
                Region = new Region(path);
            }
            old?.Dispose();
        }
        if (!Visible)
            Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
