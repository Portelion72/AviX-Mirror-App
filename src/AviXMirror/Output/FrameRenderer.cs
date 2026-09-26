using System.Drawing.Drawing2D;

namespace AviXMirror.Output;

/// <summary>Dessine une image source dans une zone cible avec rotation, miroir et mise à l'échelle.</summary>
public static class FrameRenderer
{
    public static void Draw(Graphics g, Bitmap source, Size target, int rotationDegrees, bool flip, bool stretch)
    {
        if (!stretch)
            g.Clear(Color.Black);

        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        rotationDegrees = ((rotationDegrees % 360) + 360) % 360;
        bool swap = rotationDegrees % 180 != 0;
        float lw = swap ? target.Height : target.Width;
        float lh = swap ? target.Width : target.Height;

        var saved = g.Save();
        g.TranslateTransform(target.Width / 2f, target.Height / 2f);
        if (rotationDegrees != 0)
            g.RotateTransform(rotationDegrees);
        if (flip)
            g.ScaleTransform(-1, 1);

        RectangleF dest;
        if (stretch)
        {
            dest = new RectangleF(-lw / 2, -lh / 2, lw, lh);
        }
        else
        {
            float scale = Math.Min(lw / source.Width, lh / source.Height);
            float dw = source.Width * scale, dh = source.Height * scale;
            dest = new RectangleF(-dw / 2, -dh / 2, dw, dh);
        }
        g.DrawImage(source, dest, new RectangleF(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        g.Restore(saved);
    }
}
