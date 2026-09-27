using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AviXMirror.Radar;

namespace AviXMirror.Hud;

/// <summary>Voiture derrière le joueur, dans le repère local du joueur (x = gauche, y = haut, z = arrière, en m).</summary>
public readonly record struct HudTarget(double Lx, double Ly, double Lz, double ClosingKmh);

/// <summary>
/// ATH façon caméra de recul Bosch Motorsport : flèche colorée au-dessus de chaque voiture derrière
/// (couleur selon l'écart en temps), échelle de distance à gauche et de temps à droite, alignées sur
/// la perspective de l'image, avec un repère pour chaque voiture.
/// </summary>
public static class HudRenderer
{
    const double CarLength = 4.6;
    static readonly double[] DistanceMarks = { 5, 10, 20, 30, 50, 75, 100, 150 };
    static readonly double[] TimeMarks = { 0.25, 0.5, 1, 1.5, 2, 3 };

    /// <param name="project">Repère local du joueur -> point de l'image (null si derrière la caméra).</param>
    /// <param name="groundY">Hauteur du sol dans le repère local (pour l'échelle).</param>
    /// <param name="speed">Vitesse du joueur (m/s), pour l'échelle de temps.</param>
    public static void Draw(Graphics g, int w, int h, Func<double, double, double, PointF?> project, double groundY,
        IEnumerable<HudTarget> targets, double speed, Settings s)
    {
        if (!s.HudEnabled)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var list = targets.Where(t => t.Lz > 3 && t.Lz < s.RadarRange).OrderByDescending(t => t.Lz).ToList();
        float unit = h / 400f; // tailles pensées pour un écran de 400 px de haut
        float band = 58 * unit;

        double? YOf(double distance)
        {
            var p = project(0, groundY, distance);
            return p is { } q && q.Y > 0 && q.Y < h ? q.Y : null;
        }

        if (s.HudShowScales)
        {
            DrawScale(g, 0, band, h, unit, YOf, DistanceMarks.Where(d => d <= s.RadarRange).Select(d => (d, $"{d:0} m")), left: true);
            if (speed > 5)
                DrawScale(g, w - band, band, h, unit, YOf,
                    TimeMarks.Select(t => (t * speed, t < 1 ? $"{t:0.##} s" : $"{t:0.#} s")).Where(m => m.Item1 <= s.RadarRange),
                    left: false);
        }

        foreach (var t in list)
        {
            double gapM = Math.Max(0, t.Lz - CarLength);
            double gapS = speed > 1 ? gapM / speed : double.PositiveInfinity;
            var color = GapColor(gapS);

            // Repères sur les deux échelles.
            if (s.HudShowScales && YOf(t.Lz - CarLength / 2) is double y)
            {
                DrawMarker(g, band, (float)y, unit, color, pointRight: true);
                DrawMarker(g, w - band, (float)y, unit, color, pointRight: false);
            }

            // Flèche au-dessus de la voiture (pointe vers le bas).
            var top = project(t.Lx, t.Ly + 1.4, t.Lz - CarLength / 2);
            if (top is not { } p)
                continue;
            float size = (float)Math.Clamp(900 / t.Lz, 14, 56) * unit;
            DrawArrow(g, p.X, p.Y - 4 * unit, size, color);

            string text = double.IsInfinity(gapS) ? $"{gapM:0} m" : $"{gapM:0} m · {gapS:0.0} s";
            if (t.ClosingKmh >= 5)
                text += $"  ▲{t.ClosingKmh:0}";
            using var font = new Font(Ui.Theme.FontName, Math.Clamp(size * 0.42f, 10 * unit, 18 * unit), FontStyle.Bold, GraphicsUnit.Pixel);
            DrawOutlinedText(g, text, font, new PointF(p.X, p.Y - 4 * unit - size - 2), color, bottomAnchored: true);
        }
    }

    /// <summary>Vert au-delà d'1 s, orange de 0,5 à 1 s, rouge en dessous.</summary>
    public static Color GapColor(double gapSeconds) =>
        gapSeconds < 0.5 ? Color.FromArgb(255, 60, 45)
        : gapSeconds < 1.0 ? Color.FromArgb(255, 170, 20)
        : Color.FromArgb(70, 220, 90);

    static void DrawScale(Graphics g, float x, float width, int h, float unit, Func<double, double?> yOf,
        IEnumerable<(double Distance, string Label)> marks, bool left)
    {
        using (var bg = new LinearGradientBrush(new RectangleF(x, 0, width, h),
                   left ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0),
                   left ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(150, 0, 0, 0), LinearGradientMode.Horizontal))
            g.FillRectangle(bg, x, 0, width, h);

        using var tick = new Pen(Color.FromArgb(210, 235, 235, 235), 1.5f * unit);
        using var font = new Font(Ui.Theme.FontName, 11 * unit, FontStyle.Bold, GraphicsUnit.Pixel);
        float edge = left ? x + 3 * unit : x + width - 3 * unit;
        foreach (var (distance, label) in marks)
        {
            if (yOf(distance) is not double yd)
                continue;
            float y = (float)yd;
            float len = 10 * unit;
            g.DrawLine(tick, edge, y, left ? edge + len : edge - len, y);
            var size = g.MeasureString(label, font);
            float tx = left ? edge + len + 2 * unit : edge - len - 2 * unit - size.Width;
            DrawOutlinedText(g, label, font, new PointF(tx + size.Width / 2, y), Color.FromArgb(235, 235, 235), bottomAnchored: false);
        }

        // Titre de l'échelle.
        using var titleFont = new Font(Ui.Theme.FontName, 9 * unit, FontStyle.Bold, GraphicsUnit.Pixel);
        string title = left ? "DIST." : "TEMPS";
        DrawOutlinedText(g, title, titleFont, new PointF(x + width / 2, 12 * unit), Ui.Theme.Accent, bottomAnchored: false);
    }

    static void DrawMarker(Graphics g, float x, float y, float unit, Color color, bool pointRight)
    {
        float s = 7 * unit;
        var pts = pointRight
            ? new[] { new PointF(x - s * 1.4f, y - s), new PointF(x, y), new PointF(x - s * 1.4f, y + s) }
            : new[] { new PointF(x + s * 1.4f, y - s), new PointF(x, y), new PointF(x + s * 1.4f, y + s) };
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pts);
    }

    static void DrawArrow(Graphics g, float x, float tipY, float size, Color color)
    {
        // Chevron plein pointant vers la voiture, contour sombre pour rester lisible sur l'image.
        var pts = new[]
        {
            new PointF(x - size * 0.5f, tipY - size),
            new PointF(x, tipY - size * 0.45f),
            new PointF(x + size * 0.5f, tipY - size),
            new PointF(x, tipY),
        };
        using var path = new GraphicsPath();
        path.AddPolygon(new[] { pts[0], pts[1], pts[2], pts[3] });
        using var outline = new Pen(Color.FromArgb(200, 0, 0, 0), Math.Max(2, size * 0.08f)) { LineJoin = LineJoin.Round };
        g.DrawPath(outline, path);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>Texte avec contour sombre, centré horizontalement ; ancré par le bas ou par le centre.</summary>
    static void DrawOutlinedText(Graphics g, string text, Font font, PointF anchor, Color color, bool bottomAnchored)
    {
        var size = g.MeasureString(text, font);
        var origin = new PointF(anchor.X - size.Width / 2, bottomAnchored ? anchor.Y - size.Height : anchor.Y - size.Height / 2);
        origin.Y = Math.Max(0, origin.Y);
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, origin, StringFormat.GenericDefault);
        using var outline = new Pen(Color.FromArgb(220, 0, 0, 0), Math.Max(2, font.Size * 0.18f)) { LineJoin = LineJoin.Round };
        g.DrawPath(outline, path);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }
}
