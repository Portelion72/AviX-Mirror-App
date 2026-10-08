using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

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
        DrawBackground(g, w, h);
        var logo = DrawLogo(g, w, h);
        using var line = new SolidBrush(Theme.Accent);
        g.FillRectangle(line, w / 2f - w * 0.05f, logo.Bottom + h * 0.06f, w * 0.1f, Math.Max(2, h * 0.008f));
        DrawMessage(g, w, h, logo, message);
    }

    static RectangleF LogoBox(int w, int h) => new(w * 0.2f, h * 0.14f, w * 0.6f, h * 0.56f);

    /// <summary>
    /// Fond noir uni : les dégradés sombres (halo, fond dégradé) donnaient des marches et des pixels
    /// visibles sur le VoCore (couleurs 16 bits).
    /// </summary>
    static void DrawBackground(Graphics g, int w, int h) => g.Clear(Color.Black);

    /// <summary>Logo centré, redimensionné en haute qualité, aligné sur les pixels (net, sans flou).</summary>
    static RectangleF DrawLogo(Graphics g, int w, int h)
    {
        var logo = Theme.Logo;
        var box = LogoBox(w, h);
        float scale = Math.Min(box.Width / logo.Width, box.Height / logo.Height);
        var r = new RectangleF(MathF.Round(box.X + (box.Width - logo.Width * scale) / 2), MathF.Round(box.Y + (box.Height - logo.Height * scale) / 2),
            MathF.Round(logo.Width * scale), MathF.Round(logo.Height * scale));
        lock (LogoLock)
            DrawImageHq(g, ScaledLogo(Size.Round(r.Size)), r);
        return r;
    }

    static void DrawImageHq(Graphics g, Image image, RectangleF r, ImageAttributes? attributes = null)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighQuality;
        using var wrap = attributes == null ? new ImageAttributes() : null;
        var a = attributes ?? wrap!;
        a.SetWrapMode(WrapMode.TileFlipXY); // pas de liseré sur les bords
        g.DrawImage(image, Rectangle.Round(r), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, a);
    }

    static void DrawMessage(Graphics g, int w, int h, RectangleF logo, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        // Texte lissé en niveaux de gris (le ClearType laisse des franges colorées sur le VoCore).
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var small = new Font(Theme.FontName, Math.Max(10, h * 0.045f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Theme.TextMuted);
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(message, small, brush, new RectangleF(0, logo.Bottom + h * 0.09f, w, h * 0.12f), format);
    }

    // Logo et logo éclairci, préparés une fois à la taille d'affichage (rendu bicubique de qualité).
    static readonly object LogoLock = new();
    static readonly Dictionary<Size, Bitmap> ScaledLogos = new(), ScaledBrights = new();
    static Bitmap? _sweep;

    /// <summary>Cache par taille (VoCore, coin, aperçus du tutoriel) ; à utiliser sous <see cref="LogoLock"/>.</summary>
    static Bitmap Cached(Dictionary<Size, Bitmap> cache, Size size, Func<Bitmap> create)
    {
        if (cache.TryGetValue(size, out var bmp))
            return bmp;
        if (cache.Count >= 8)
        {
            foreach (var old in cache.Values)
                old.Dispose();
            cache.Clear();
        }
        return cache[size] = create();
    }

    static Bitmap ScaledLogo(Size size) => Cached(ScaledLogos, size, () => Resample(Theme.Logo, size, null));

    /// <summary>Logo éclairci (pour le reflet), à la taille d'affichage.</summary>
    static Bitmap ScaledBright(Size size) => Cached(ScaledBrights, size, () =>
    {
        using var attributes = new ImageAttributes();
        // Couleurs rapprochées du blanc, transparence du logo conservée.
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { 0.35f, 0, 0, 0, 0 },
            new[] { 0, 0.35f, 0, 0, 0 },
            new[] { 0, 0, 0.35f, 0, 0 },
            new[] { 0, 0, 0, 1f, 0 },
            new[] { 0.65f, 0.65f, 0.65f, 0, 1 },
        }));
        return Resample(Theme.Logo, size, attributes);
    });

    static Bitmap Resample(Image source, Size size, ImageAttributes? attributes)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var wrap = new ImageAttributes();
        var a = attributes ?? wrap;
        a.SetWrapMode(WrapMode.TileFlipXY);
        lock (source)
            g.DrawImage(source, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, a);
        return bmp;
    }

    /// <summary>
    /// Reflet : le logo éclairci, visible seulement dans une bande inclinée aux bords très doux
    /// (transparence calculée pixel par pixel : pas d'escalier sur les bords de la bande).
    /// </summary>
    static unsafe Bitmap Sweep(Size size, double center, float strength)
    {
        var bright = ScaledBright(size);
        if (_sweep == null || _sweep.Size != size)
        {
            _sweep?.Dispose();
            _sweep = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }
        int w = size.Width, h = size.Height;
        double half = w * 0.09;          // demi-largeur de la bande
        double slope = 0.55;             // inclinaison (décalage horizontal par pixel de hauteur)
        var rect = new Rectangle(0, 0, w, h);
        var src = bright.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dst = _sweep.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                uint* s = (uint*)((byte*)src.Scan0 + (long)y * src.Stride);
                uint* d = (uint*)((byte*)dst.Scan0 + (long)y * dst.Stride);
                double bandCenter = center + (h / 2.0 - y) * slope;
                for (int x = 0; x < w; x++)
                {
                    double t = Math.Abs(x - bandCenter) / half;
                    if (t >= 1)
                    {
                        d[x] = 0;
                        continue;
                    }
                    // Profil en cloche (cosinus) : maximum au centre, nul et plat aux bords.
                    double weight = 0.5 + 0.5 * Math.Cos(t * Math.PI);
                    uint c = s[x];
                    uint alpha = (uint)((c >> 24) * weight * strength);
                    d[x] = (alpha << 24) | (c & 0x00FFFFFF);
                }
            }
        }
        finally
        {
            bright.UnlockBits(src);
            _sweep.UnlockBits(dst);
        }
        return _sweep;
    }

    /// <summary>
    /// Page de veille animée (jeu en pause, en attente) sur fond noir : reflet doux qui balaie le logo
    /// et trait d'accent qui va et vient. <paramref name="time"/> en secondes.
    /// </summary>
    public static void DrawAnimated(Bitmap bmp, double time, string message)
    {
        using var g = Graphics.FromImage(bmp);
        int w = bmp.Width, h = bmp.Height;
        Theme.Smooth(g);
        DrawBackground(g, w, h);
        var logo = DrawLogo(g, w, h);

        // Reflet : traverse le logo en 1,6 s, toutes les 3,2 s, avec une entrée et une sortie en douceur.
        double cycle = time % 3.2 / 1.6;
        if (cycle < 1)
        {
            double eased = cycle * cycle * (3 - 2 * cycle);
            var size = Size.Round(logo.Size);
            double travel = size.Width + size.Height * 1.5;
            double center = -size.Height * 0.75 + eased * travel;
            lock (LogoLock)
                DrawImageHq(g, Sweep(size, center, 0.9f), logo);
        }

        // Trait d'accent : un segment qui glisse d'un bord à l'autre sous le logo, bords arrondis.
        float trackW = w * 0.24f, trackY = logo.Bottom + h * 0.06f, thick = Math.Max(3, h * 0.009f);
        float trackX = w / 2f - trackW / 2;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (var trackPath = Theme.RoundedRect(new RectangleF(trackX, trackY, trackW, thick), thick / 2))
        using (var track = new SolidBrush(Theme.Blend(Color.Black, Theme.Accent, 0.25f)))
            g.FillPath(track, trackPath);
        double phase = (Math.Sin(time * 2.2) + 1) / 2;
        float segW = trackW * 0.3f;
        using (var segPath = Theme.RoundedRect(new RectangleF(trackX + (float)phase * (trackW - segW), trackY, segW, thick), thick / 2))
        using (var seg = new SolidBrush(Theme.Accent))
            g.FillPath(seg, segPath);

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
        var r = new RectangleF(MathF.Round(box.X), MathF.Round(box.Y), MathF.Round(box.Width), MathF.Round(box.Height));
        lock (LogoLock)
            DrawImageHq(g, ScaledLogo(Size.Round(r.Size)), r);
    }
}
