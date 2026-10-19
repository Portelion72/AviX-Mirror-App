namespace AviXMirror.Capture;

/// <summary>Source d'image du mode Capture : fenêtre du jeu (Windows.Graphics.Capture) ou add-on ReShade.</summary>
public interface ICaptureSource : IDisposable
{
    /// <summary>Taille de l'image source entière.</summary>
    Size SourceSize { get; }

    /// <summary>Vrai pendant la calibration : image entière, sans recadrage.</summary>
    bool FullFrame { get; set; }

    /// <summary>Zone(s) recadrée(s) ; une deuxième zone met deux rétros côte à côte.</summary>
    void SetCrops(Rectangle crop, Rectangle crop2, DualFit fit = DualFit.Entier);
}

/// <summary>Mise en page des zones capturées dans l'image envoyée au VoCore.</summary>
public static class FrameComposer
{
    /// <summary>
    /// Écrit dans <paramref name="output"/> l'image source entière, la zone recadrée, ou deux zones côte à côte
    /// (chacune dans une moitié au format 640 × 400).
    /// </summary>
    public static void Write(Util.FrameBuffer output, Bitmap source, bool full, Rectangle crop, Rectangle crop2, DualFit fit)
    {
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var a = Rectangle.Intersect(crop, bounds);
        var b = Rectangle.Intersect(crop2, bounds);
        if (!full && a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0)
        {
            int outH = Math.Max(a.Height, b.Height), half = (int)Math.Round(outH * 1.6);
            output.Write(half * 2, outH, bmp =>
            {
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.Black);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                DrawFitted(g, source, a, new Rectangle(0, 0, half, outH), fit);
                DrawFitted(g, source, b, new Rectangle(half, 0, half, outH), fit);
            });
            return;
        }
        var region = !full && a.Width > 0 && a.Height > 0 ? a : bounds;
        output.Write(region.Width, region.Height, bmp =>
        {
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
        });
    }

    /// <summary>Place une zone (rétro) dans sa moitié : entière, en remplissant (recadrée au centre) ou étirée.</summary>
    public static void DrawFitted(Graphics g, Bitmap source, Rectangle src, Rectangle dest, DualFit fit)
    {
        if (src.Width <= 0 || src.Height <= 0)
            return;
        switch (fit)
        {
            case DualFit.Etirer:
                g.DrawImage(source, dest, src, GraphicsUnit.Pixel);
                break;
            case DualFit.Remplir:
            {
                // Garde le centre de la zone au format de la moitié.
                double target = dest.Width / (double)dest.Height;
                var crop = src;
                if (src.Width / (double)src.Height > target)
                {
                    int w = (int)Math.Round(src.Height * target);
                    crop = new Rectangle(src.X + (src.Width - w) / 2, src.Y, w, src.Height);
                }
                else
                {
                    int h = (int)Math.Round(src.Width / target);
                    crop = new Rectangle(src.X, src.Y + (src.Height - h) / 2, src.Width, h);
                }
                g.DrawImage(source, dest, crop, GraphicsUnit.Pixel);
                break;
            }
            default:
            {
                double scale = Math.Min(dest.Width / (double)src.Width, dest.Height / (double)src.Height);
                int w = (int)Math.Round(src.Width * scale), h = (int)Math.Round(src.Height * scale);
                g.DrawImage(source, new Rectangle(dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h), src, GraphicsUnit.Pixel);
                break;
            }
        }
    }

}
