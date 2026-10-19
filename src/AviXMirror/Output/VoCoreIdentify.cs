using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AviXMirror.Util;

namespace AviXMirror.Output;

/// <summary>
/// « Identifier les écrans » (comme SimHub) : chaque VoCore branché affiche quelques secondes un grand
/// numéro et son identifiant, pour savoir lequel choisir. Les écrans doivent être libres (rétro arrêté,
/// SimHub fermé ou VoCore désactivé dans SimHub).
/// </summary>
public static class VoCoreIdentify
{
    /// <summary>Affiche les numéros (bloquant, environ <paramref name="seconds"/> s). Retourne chaque écran et s'il a répondu.</summary>
    public static List<(int Number, VoCoreDeviceInfo Device, bool Shown)> Run(Settings settings, double seconds = 5)
    {
        var devices = VoCoreDevices.List(settings);
        var outputs = new List<(int Number, VoCoreDeviceInfo Device, FrameBuffer Frames, VoCoreUsbOutput Output)>();
        try
        {
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                var s = settings.Clone();
                s.VoCoreDevice = device.Key;
                s.LedsEnabled = false; // pas de balayage des LEDs pendant l'identification
                var frames = new FrameBuffer();
                int number = i + 1;
                bool chosen = device.Matches(settings.VoCoreDevice) || (string.IsNullOrWhiteSpace(settings.VoCoreDevice) && i == 0);
                frames.Write(1280, 400, bmp => Draw(bmp, number, device, chosen));
                outputs.Add((number, device, frames, new VoCoreUsbOutput(frames, s)));
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
                Thread.Sleep(100);
            return outputs.Select(o => (o.Number, o.Device, o.Output.Device != null)).ToList();
        }
        finally
        {
            foreach (var o in outputs)
            {
                o.Output.Dispose();
                o.Frames.Dispose();
            }
        }
    }

    static void Draw(Bitmap bmp, int number, VoCoreDeviceInfo device, bool chosen)
    {
        using var g = Graphics.FromImage(bmp);
        int w = bmp.Width, h = bmp.Height;
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var accent = new SolidBrush(Ui.Theme.Accent);
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Ui.Theme.TextMuted);
        using var big = new Font(Ui.Theme.FontName, h * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var title = new Font(Ui.Theme.FontName, h * 0.11f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new Font(Ui.Theme.FontName, h * 0.07f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString(number.ToString(), big, accent, new RectangleF(0, 0, w * 0.32f, h), center);
        float x = w * 0.34f;
        g.DrawString($"ÉCRAN {number}", title, white, x, h * 0.2f);
        g.DrawString(device.Label, text, muted, x, h * 0.42f);
        g.DrawString(chosen ? "Écran utilisé par AviX Mirror" : "Non utilisé par AviX Mirror", text, chosen ? accent : muted, x, h * 0.58f);

        using var pen = new Pen(Ui.Theme.Accent, Math.Max(3, h * 0.012f));
        g.DrawRectangle(pen, pen.Width / 2, pen.Width / 2, w - pen.Width, h - pen.Width);
    }
}
