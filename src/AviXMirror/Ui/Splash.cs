using System.Drawing.Drawing2D;

namespace AviXMirror.Ui;

/// <summary>Écran d'accueil affiché sur le VoCore en attendant la première image.</summary>
public static class Splash
{
    public static void Draw(Bitmap bmp, string message)
    {
        using var g = Graphics.FromImage(bmp);
        Theme.Smooth(g);
        int w = bmp.Width, h = bmp.Height;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Theme.Background, Color.Black, LinearGradientMode.Vertical))
            g.FillRectangle(bg, 0, 0, w, h);

        // Logotype centré : « AVIX » blanc + « _3D » accent, puis « MIRROR ».
        using var logo = new Font(Theme.FontName, h * 0.20f, FontStyle.Bold, GraphicsUnit.Pixel);
        var sizeA = TextRenderer.MeasureText(g, "AVIX", logo, Size.Empty, TextFormatFlags.NoPadding);
        var sizeB = TextRenderer.MeasureText(g, "_3D", logo, Size.Empty, TextFormatFlags.NoPadding);
        int x = (w - sizeA.Width - sizeB.Width) / 2, y = (int)(h * 0.26f);
        TextRenderer.DrawText(g, "AVIX", logo, new Point(x, y), Theme.Text, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "_3D", logo, new Point(x + sizeA.Width, y), Theme.Accent, TextFormatFlags.NoPadding);

        using var product = new Font(Theme.FontName, h * 0.07f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "M I R R O R", product, new Rectangle(0, y + sizeA.Height + (int)(h * 0.02f), w, (int)(h * 0.1f)),
            Theme.TextMuted, TextFormatFlags.HorizontalCenter);

        using var small = new Font(Theme.FontName, h * 0.05f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, message, small, new Rectangle(0, (int)(h * 0.80f), w, (int)(h * 0.12f)),
            Theme.TextMuted, TextFormatFlags.HorizontalCenter);

        using var line = new SolidBrush(Theme.Accent);
        g.FillRectangle(line, w * 0.42f, h * 0.73f, w * 0.16f, Math.Max(2, h * 0.008f));
    }
}
