using System.Drawing.Imaging;

namespace AviXMirror.Util;

/// <summary>
/// Accentuation adaptative des contours (principe d'AMD FidelityFX CAS) : chaque pixel est renforcé par rapport à
/// ses 4 voisins, d'autant moins que la zone est déjà contrastée (pas de halo ni de bruit amplifié). Appliquée sur
/// la petite image capturée avant son agrandissement, elle redonne du piqué au rétro sur le VoCore.
/// </summary>
public static class ImageSharpen
{
    [ThreadStatic] static byte[]? _copy;

    public static unsafe void Apply(Bitmap bmp, float sharpness)
    {
        int w = bmp.Width, h = bmp.Height;
        if (w < 3 || h < 3)
            return;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
        try
        {
            int stride = data.Stride, size = stride * h;
            var copy = _copy != null && _copy.Length >= size ? _copy : _copy = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, copy, 0, size);
            // Poids négatif des voisins : de -1/8 (doux) à -1/5 (fort), modulé par le contraste local.
            float peak = -1f / (8f - 3f * Math.Clamp(sharpness, 0, 1));
            byte* dst = (byte*)data.Scan0;
            fixed (byte* src = copy)
            {
                for (int y = 1; y < h - 1; y++)
                {
                    byte* row = src + y * stride, up = row - stride, down = row + stride;
                    byte* outRow = dst + y * stride;
                    for (int x = 1; x < w - 1; x++)
                    {
                        int o = x * 4;
                        // Contraste local sur la luminance (vert) : min et max de la croix.
                        int c = row[o + 1], n = up[o + 1], s = down[o + 1], e = row[o + 5], wv = row[o - 3];
                        int mn = Math.Min(c, Math.Min(Math.Min(n, s), Math.Min(e, wv)));
                        int mx = Math.Max(c, Math.Max(Math.Max(n, s), Math.Max(e, wv)));
                        if (mx == 0)
                            continue;
                        float amp = MathF.Sqrt(Math.Clamp(Math.Min(mn, 255 - mx) / (float)mx, 0, 1));
                        float wgt = amp * peak;
                        float norm = 1f / (1f + 4f * wgt);
                        for (int ch = 0; ch < 3; ch++)
                        {
                            int i = o + ch;
                            float v = (row[i] + wgt * (up[i] + down[i] + row[i + 4] + row[i - 4])) * norm;
                            outRow[i] = (byte)Math.Clamp((int)(v + 0.5f), 0, 255);
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
