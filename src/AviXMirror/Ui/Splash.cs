using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AviXMirror.Ui;

/// <summary>Page de veille du VoCore : logo AVIX sur fond sombre, fixe ou animée.</summary>
public static class Splash
{
    public static void Draw(Bitmap bmp, string message)
    {
        using var g = Graphics.FromImage(bmp);
        Draw(g, bmp.Width, bmp.Height, message);
    }

    public static void Draw(Graphics g, int w, int h, string message)
    {
        Theme.Smooth(g);
        DrawBackground(g, w, h, 40);
        var logo = Theme.DrawLogo(g, LogoBox(w, h));
        using var line = new SolidBrush(Theme.Accent);
        g.FillRectangle(line, w / 2f - w * 0.05f, logo.Bottom + h * 0.06f, w * 0.1f, Math.Max(2, h * 0.008f));
        DrawMessage(g, w, h, logo, message);
    }

    static RectangleF LogoBox(int w, int h) => new(w * 0.2f, h * 0.14f, w * 0.6f, h * 0.56f);

    static void DrawBackground(Graphics g, int w, int h, int haloAlpha)
    {
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(18, 19, 22), Color.Black, LinearGradientMode.Vertical))
            g.FillRectangle(bg, 0, 0, w, h);

        // Halo couleur d'accent derrière le logo.
        using var path = new GraphicsPath();
        path.AddEllipse(w * 0.2f, h * 0.05f, w * 0.6f, h * 0.9f);
        using var halo = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(Math.Clamp(haloAlpha, 0, 255), Theme.Accent),
            SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
        };
        g.FillPath(halo, path);
    }

    static void DrawMessage(Graphics g, int w, int h, RectangleF logo, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        using var small = new Font(Theme.FontName, Math.Max(10, h * 0.045f), FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, message, small, new Rectangle(0, (int)(logo.Bottom + h * 0.09f), w, (int)(h * 0.1f)),
            Theme.TextMuted, TextFormatFlags.HorizontalCenter);
    }

    static Bitmap? _brightLogo;

    /// <summary>Logo éclairci (reflet qui balaie le logo), préparé une fois.</summary>
    static Bitmap BrightLogo()
    {
        if (_brightLogo != null)
            return _brightLogo;
        var logo = Theme.Logo;
        var bmp = new Bitmap(logo.Width, logo.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var attributes = new ImageAttributes())
        {
            // Couleurs rapprochées du blanc, transparence du logo conservée.
            attributes.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { 0.4f, 0, 0, 0, 0 },
                new[] { 0, 0.4f, 0, 0, 0 },
                new[] { 0, 0, 0.4f, 0, 0 },
                new[] { 0, 0, 0, 1f, 0 },
                new[] { 0.6f, 0.6f, 0.6f, 0, 1 },
            }));
            lock (logo)
                g.DrawImage(logo, new Rectangle(0, 0, logo.Width, logo.Height), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
        }
        return _brightLogo = bmp;
    }

    /// <summary>
    /// Page de veille animée (jeu en pause, en attente) : halo qui respire, reflet qui balaie le logo
    /// et trait d'accent qui va et vient. <paramref name="time"/> en secondes.
    /// </summary>
    public static void DrawAnimated(Bitmap bmp, double time, string message)
    {
        using var g = Graphics.FromImage(bmp);
        int w = bmp.Width, h = bmp.Height;
        Theme.Smooth(g);

        int haloAlpha = (int)(34 + 26 * Math.Sin(time * 1.7));
        DrawBackground(g, w, h, haloAlpha);
        var logo = Theme.DrawLogo(g, LogoBox(w, h));

        // Reflet : bande inclinée qui traverse le logo toutes les 3,2 s (puis une pause).
        double cycle = time % 3.2 / 2.0;
        if (cycle < 1)
        {
            float bandX = logo.Left - logo.Height + (float)cycle * (logo.Width + 2 * logo.Height);
            var bright = BrightLogo();
            foreach (var (width, alpha) in new[] { (0.16f, 0.35f), (0.08f, 0.7f), (0.03f, 1f) })
            {
                float bw = logo.Width * width;
                using var band = new GraphicsPath();
                band.AddPolygon(new[]
                {
                    new PointF(bandX - bw / 2 + logo.Height * 0.5f, logo.Top),
                    new PointF(bandX + bw / 2 + logo.Height * 0.5f, logo.Top),
                    new PointF(bandX + bw / 2 - logo.Height * 0.5f, logo.Bottom),
                    new PointF(bandX - bw / 2 - logo.Height * 0.5f, logo.Bottom),
                });
                var state = g.Save();
                g.SetClip(band);
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                g.DrawImage(bright, Rectangle.Round(logo), 0, 0, bright.Width, bright.Height, GraphicsUnit.Pixel, attributes);
                g.Restore(state);
            }
        }

        // Trait d'accent : un segment qui glisse d'un bord à l'autre sous le logo.
        float trackW = w * 0.24f, trackY = logo.Bottom + h * 0.06f, thick = Math.Max(2, h * 0.008f);
        float trackX = w / 2f - trackW / 2;
        using (var track = new SolidBrush(Color.FromArgb(50, Theme.Accent)))
            g.FillRectangle(track, trackX, trackY, trackW, thick);
        double phase = (Math.Sin(time * 2.2) + 1) / 2;
        float segW = trackW * 0.3f;
        using (var seg = new SolidBrush(Theme.Accent))
            g.FillRectangle(seg, trackX + (float)phase * (trackW - segW), trackY, segW, thick);

        DrawMessage(g, w, h, logo, message);
    }
}
