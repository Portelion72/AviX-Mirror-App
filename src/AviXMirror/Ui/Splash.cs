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

    // ---------- Image ou animation personnalisée ----------

    sealed class CustomImage : IDisposable
    {
        public required Image Image;
        public required string Path;
        public required DateTime Modified;
        public int Frames = 1;
        public double[] FrameEnds = Array.Empty<double>(); // fin de chaque image (s)

        public void Dispose() => Image.Dispose();
    }

    static readonly object CustomLock = new();
    static CustomImage? _custom;

    /// <summary>Charge (une fois) l'image ou le GIF animé ; null si le fichier est illisible.</summary>
    static CustomImage? LoadCustom(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var modified = File.GetLastWriteTimeUtc(path);
        if (_custom != null && _custom.Path == path && _custom.Modified == modified)
            return _custom;
        _custom?.Dispose();
        _custom = null;
        try
        {
            // Copie en mémoire : le fichier reste modifiable pendant que l'application tourne.
            var image = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
            var custom = new CustomImage { Image = image, Path = path, Modified = modified };
            var dimension = System.Drawing.Imaging.FrameDimension.Time;
            if (image.FrameDimensionsList.Contains(dimension.Guid))
            {
                custom.Frames = image.GetFrameCount(dimension);
                // Durées des images d'un GIF (propriété 0x5100, en centièmes de seconde).
                var delays = image.PropertyIdList.Contains(0x5100) ? image.GetPropertyItem(0x5100)?.Value : null;
                custom.FrameEnds = new double[custom.Frames];
                double total = 0;
                for (int i = 0; i < custom.Frames; i++)
                {
                    int delay = delays != null && delays.Length >= (i + 1) * 4 ? BitConverter.ToInt32(delays, i * 4) : 10;
                    total += Math.Max(2, delay) / 100.0;
                    custom.FrameEnds[i] = total;
                }
            }
            return _custom = custom;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Écran de veille personnalisé : image ou animation GIF (au temps <paramref name="time"/>), puis petit
    /// logo AVIX en bas à droite. Retourne faux si l'image est introuvable (l'écran AVIX s'affiche alors).
    /// </summary>
    public static bool DrawCustom(Bitmap bmp, string path, bool fill, double time)
    {
        lock (CustomLock)
        {
            var custom = LoadCustom(path);
            if (custom == null)
                return false;
            var image = custom.Image;
            if (custom.Frames > 1 && custom.FrameEnds.Length == custom.Frames)
            {
                double t = time % custom.FrameEnds[^1];
                int frame = Array.FindIndex(custom.FrameEnds, end => t < end);
                image.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, Math.Max(0, frame));
            }

            using var g = Graphics.FromImage(bmp);
            int w = bmp.Width, h = bmp.Height;
            Theme.Smooth(g);
            g.Clear(Color.Black);
            float scale = fill
                ? Math.Max(w / (float)image.Width, h / (float)image.Height)
                : Math.Min(w / (float)image.Width, h / (float)image.Height);
            float iw = image.Width * scale, ih = image.Height * scale;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, (w - iw) / 2, (h - ih) / 2, iw, ih);
            DrawCornerLogo(g, w, h);
            return true;
        }
    }

    /// <summary>Logo AVIX en petit, en bas à droite, sur un léger fond pour rester lisible.</summary>
    public static void DrawCornerLogo(Graphics g, int w, int h)
    {
        var logo = Theme.Logo;
        float lw = w * 0.14f, lh = lw * logo.Height / logo.Width;
        if (lh > h * 0.2f)
        {
            lh = h * 0.2f;
            lw = lh * logo.Width / logo.Height;
        }
        float margin = h * 0.035f;
        var box = new RectangleF(w - lw - margin, h - lh - margin, lw, lh);
        using (var shade = Theme.RoundedRect(RectangleF.Inflate(box, h * 0.02f, h * 0.015f), h * 0.02f))
        using (var brush = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            g.FillPath(brush, shade);
        Theme.DrawLogo(g, box);
    }
}
