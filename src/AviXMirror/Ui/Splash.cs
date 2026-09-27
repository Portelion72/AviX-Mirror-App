using System.Drawing.Drawing2D;

namespace AviXMirror.Ui;

/// <summary>Page de veille du VoCore : logo AVIX sur fond sombre, en attendant le jeu.</summary>
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
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(18, 19, 22), Color.Black, LinearGradientMode.Vertical))
            g.FillRectangle(bg, 0, 0, w, h);

        // Halo discret couleur d'accent derrière le logo.
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(w * 0.2f, h * 0.05f, w * 0.6f, h * 0.9f);
            using var halo = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(40, Theme.Accent),
                SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
            };
            g.FillPath(halo, path);
        }

        var logo = Theme.DrawLogo(g, new RectangleF(w * 0.2f, h * 0.14f, w * 0.6f, h * 0.56f));

        using var line = new SolidBrush(Theme.Accent);
        g.FillRectangle(line, w / 2f - w * 0.05f, logo.Bottom + h * 0.06f, w * 0.1f, Math.Max(2, h * 0.008f));

        if (!string.IsNullOrEmpty(message))
        {
            using var small = new Font(Theme.FontName, Math.Max(10, h * 0.045f), FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, message, small, new Rectangle(0, (int)(logo.Bottom + h * 0.09f), w, (int)(h * 0.1f)),
                Theme.TextMuted, TextFormatFlags.HorizontalCenter);
        }
    }
}
