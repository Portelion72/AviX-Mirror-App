using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AviXMirror.Radar;

namespace AviXMirror.Hud;

/// <summary>Voiture derrière le joueur, dans le repère local du joueur (x = gauche, y = haut, z = arrière, en m).</summary>
public readonly record struct HudTarget(double Lx, double Ly, double Lz, double ClosingKmh);

/// <summary>
/// ATH façon caméra de recul Bosch Motorsport : flèche colorée au-dessus de chaque voiture derrière
/// (couleur selon l'écart en temps), échelles fixes de distance à gauche et de temps à droite sur toute
/// la hauteur de l'écran, avec un repère pour chaque voiture.
/// </summary>
public static class HudRenderer
{
    const double CarLength = 4.6;

    /// <param name="project">Repère local du joueur -> point de l'image (null si derrière la caméra).</param>
    /// <param name="groundY">Hauteur du sol dans le repère local (inutilisé depuis les échelles fixes).</param>
    /// <param name="speed">Vitesse du joueur (m/s), pour l'échelle de temps.</param>
    public static void Draw(Graphics g, int w, int h, Func<double, double, double, PointF?> project, double groundY,
        IEnumerable<HudTarget> targets, double speed, Settings s)
    {
        if (!s.HudEnabled)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float unit = h / 400f; // tailles pensées pour un écran de 400 px de haut
        float band = 96 * unit;

        // Échelles fixes sur toute la hauteur de l'écran, 10 graduations égales : distance à gauche
        // (0 en bas -> HudScaleDistance en haut) et écart en temps à droite (0 -> HudScaleTime).
        bool scales = s.HudShowScales;
        double maxD = Math.Max(1, s.HudScaleDistance), maxT = Math.Max(0.1, s.HudScaleTime);
        float yTop = 3 * unit, yBottom = h - 3 * unit;
        var list = targets.Where(t => t.Lz > 3 && t.Lz < Math.Max(s.RadarRange, maxD + CarLength)).OrderByDescending(t => t.Lz).ToList();
        float YAt(double fraction) => yBottom - (float)Math.Clamp(fraction, 0, 1) * (yBottom - yTop);
        if (scales)
        {
            var dTicks = Enumerable.Range(0, 11).Select(i => (YAt(i / 10.0), i == 0 ? "" : $"{maxD * i / 10:0.#} m"));
            var tTicks = Enumerable.Range(0, 11).Select(i => (YAt(i / 10.0), i == 0 ? "" : $"{maxT * i / 10:0.0#} s"));
            DrawScale(g, 0, band, yTop, yBottom, unit, dTicks, left: true, s.HudMirrorScales);
            DrawScale(g, w - band, band, yTop, yBottom, unit, tTicks, left: false, s.HudMirrorScales);
        }

        foreach (var t in list)
        {
            double gapM = Math.Max(0, t.Lz - CarLength);
            double gapS = speed > 1 ? gapM / speed : double.PositiveInfinity;
            var color = GapColor(gapS);

            // Repères sur les deux échelles (distance à gauche, temps à droite).
            if (scales && gapM <= maxD)
                DrawMarker(g, band, YAt(gapM / maxD), unit, color, pointRight: true);
            if (scales && gapS <= maxT)
                DrawMarker(g, w - band, YAt(gapS / maxT), unit, color, pointRight: false);

            // Flèche au-dessus de la voiture (pointe vers le bas), sans texte.
            var top = project(t.Lx, t.Ly + 1.4, t.Lz - CarLength / 2);
            if (top is not { } p)
                continue;
            float size = (float)Math.Clamp(900 / t.Lz, 14, 56) * unit;
            DrawArrow(g, p.X, p.Y - 4 * unit, size, color);
        }
    }

    /// <summary>Vert au-delà d'1 s, orange de 0,5 à 1 s, rouge en dessous.</summary>
    public static Color GapColor(double gapSeconds) =>
        gapSeconds < 0.5 ? Color.FromArgb(255, 60, 45)
        : gapSeconds < 1.0 ? Color.FromArgb(255, 170, 20)
        : Color.FromArgb(70, 220, 90);

    static void DrawScale(Graphics g, float x, float width, float yTop, float yBottom, float unit,
        IEnumerable<(float Y, string Label)> ticks, bool left, bool mirrored)
    {
        using (var bg = new LinearGradientBrush(new RectangleF(x, 0, width, yBottom + yTop + 1),
                   left ? Color.FromArgb(170, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0),
                   left ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(170, 0, 0, 0), LinearGradientMode.Horizontal))
            g.FillRectangle(bg, x, 0, width, yBottom + yTop);

        // Effet miroir : graduations et chiffres retournés dans leur bande, lisibles à l'endroit
        // quand l'image est vue en reflet (rétroviseur).
        var saved = g.Transform;
        if (mirrored)
        {
            g.TranslateTransform(2 * x + width, 0);
            g.ScaleTransform(-1, 1);
        }

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
            if (label.Length == 0)
                continue;
            // Libellé gardé dans l'écran (graduation du haut).
            float ly = Math.Max(y, yTop + size.Height / 2);
            DrawOutlinedText(g, label, font, new PointF(cx, ly), Color.FromArgb(245, 245, 245), bottomAnchored: false);
        }
        g.Transform = saved;
        saved.Dispose();
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
