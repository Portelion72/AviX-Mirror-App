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
        float band = 96 * unit;

        // Position à l'écran d'un point du sol à une distance donnée derrière (null si derrière la caméra).
        double? RawY(double distance) => project(0, groundY, distance)?.Y;
        double? YOf(double distance) => RawY(distance) is double y && y > 0 && y < h ? y : null;

        // Échelles : de la distance la plus proche visible (bas de l'image) jusqu'à la portée, découpées
        // en 10 intervalles égaux à l'écran ; chaque graduation indique sa distance (gauche) et son temps (droite).
        var ticks = new List<(float Y, double Distance)>();
        float yTop = 0, yBottom = h;
        if (s.HudShowScales && ComputeScale(RawY, h, s.RadarRange, out yTop, out yBottom, out var distanceAt))
        {
            for (int i = 1; i <= 10; i++)
            {
                float y = yBottom - i * (yBottom - yTop) / 10f;
                ticks.Add((y, distanceAt(y)));
            }
            DrawScale(g, 0, band, yTop, yBottom, unit, ticks.Select(t => (t.Y, $"{t.Distance:0} m")), left: true);
            if (speed > 5)
                DrawScale(g, w - band, band, yTop, yBottom, unit, ticks.Select(t => (t.Y, $"{t.Distance / speed:0.0} s")), left: false);
        }

        foreach (var t in list)
        {
            double gapM = Math.Max(0, t.Lz - CarLength);
            double gapS = speed > 1 ? gapM / speed : double.PositiveInfinity;
            var color = GapColor(gapS);

            // Repères sur les deux échelles.
            if (ticks.Count > 0 && YOf(t.Lz - CarLength / 2) is double y && y >= yTop && y <= yBottom)
            {
                DrawMarker(g, band, (float)y, unit, color, pointRight: true);
                DrawMarker(g, w - band, (float)y, unit, color, pointRight: false);
            }

            // Flèche au-dessus de la voiture (pointe vers le bas), sans texte.
            var top = project(t.Lx, t.Ly + 1.4, t.Lz - CarLength / 2);
            if (top is not { } p)
                continue;
            float size = (float)Math.Clamp(900 / t.Lz, 14, 56) * unit;
            DrawArrow(g, p.X, p.Y - 4 * unit, size, color);
        }
    }

    /// <summary>
    /// Bornes verticales de l'échelle et fonction inverse « hauteur à l'écran -> distance » (par dichotomie,
    /// la hauteur diminuant quand la distance augmente).
    /// </summary>
    internal static bool ComputeScale(Func<double, double?> rawY, int h, double range, out float yTop, out float yBottom,
        out Func<float, double> distanceAt)
    {
        yTop = 0; yBottom = h; distanceAt = _ => 0;
        if (rawY(range) is not double far)
            return false;

        // Première distance visible par la caméra (la caméra peut être derrière la voiture).
        double dMin = 0.6;
        while (dMin < range && rawY(dMin) is null)
            dMin += 0.25;
        if (rawY(dMin) is not double nearest)
            return false;

        // Hauteur -> distance par dichotomie (la hauteur diminue quand la distance augmente).
        double Solve(double y, double lo)
        {
            double hi = range;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) / 2;
                if (rawY(mid) is double ym && ym > y) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // Si le sol le plus proche est sous l'image, l'échelle commence au bas de l'image.
        if (nearest > h - 2)
        {
            dMin = Solve(h - 2, dMin);
            nearest = h - 2;
        }
        double start = dMin;

        yBottom = (float)Math.Min(h - 2, nearest);
        yTop = (float)Math.Max(2, far);
        if (yBottom - yTop < 40)
            return false;
        distanceAt = y => Solve(y, start);
        return true;
    }

    /// <summary>Vert au-delà d'1 s, orange de 0,5 à 1 s, rouge en dessous.</summary>
    public static Color GapColor(double gapSeconds) =>
        gapSeconds < 0.5 ? Color.FromArgb(255, 60, 45)
        : gapSeconds < 1.0 ? Color.FromArgb(255, 170, 20)
        : Color.FromArgb(70, 220, 90);

    static void DrawScale(Graphics g, float x, float width, float yTop, float yBottom, float unit,
        IEnumerable<(float Y, string Label)> ticks, bool left)
    {
        using (var bg = new LinearGradientBrush(new RectangleF(x, 0, width, yBottom + 1),
                   left ? Color.FromArgb(170, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0),
                   left ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(170, 0, 0, 0), LinearGradientMode.Horizontal))
            g.FillRectangle(bg, x, 0, width, yBottom);

        float edge = left ? x + 5 * unit : x + width - 5 * unit;
        using var spine = new Pen(Color.FromArgb(230, 235, 235, 235), 3 * unit);
        g.DrawLine(spine, edge, yTop, edge, yBottom);

        using var tick = new Pen(Color.FromArgb(240, 245, 245, 245), 3 * unit);
        using var font = new Font(Ui.Theme.FontName, 17 * unit, FontStyle.Bold, GraphicsUnit.Pixel);
        float len = 22 * unit;
        foreach (var (y, label) in ticks)
        {
            g.DrawLine(tick, edge, y, left ? edge + len : edge - len, y);
            var size = g.MeasureString(label, font);
            float cx = left ? edge + len + 3 * unit + size.Width / 2 : edge - len - 3 * unit - size.Width / 2;
            DrawOutlinedText(g, label, font, new PointF(cx, y), Color.FromArgb(245, 245, 245), bottomAnchored: false);
        }

        using var titleFont = new Font(Ui.Theme.FontName, 12 * unit, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawOutlinedText(g, left ? "DIST." : "TEMPS", titleFont, new PointF(x + width / 2, Math.Max(10 * unit, yTop - 12 * unit)),
            Ui.Theme.Accent, bottomAnchored: false);
    }

    static void DrawMarker(Graphics g, float x, float y, float unit, Color color, bool pointRight)
    {
        float s = 11 * unit;
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
