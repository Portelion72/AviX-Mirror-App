using System.Diagnostics;
using System.Drawing.Imaging;

namespace AviXMirror.Capture;

/// <summary>
/// Conversion d'une image HDR (demi-flottants RGBA, lumière linéaire) en image affichable :
/// exposition automatique (comme une vraie caméra), courbe filmique ACES, puis encodage sRGB.
/// Évite les images blanches (ciel, soleil écrêtés) et les images trop sombres.
/// </summary>
public sealed class ToneMapper
{
    const double Adaptation = 0.35;   // temps d'adaptation de l'exposition (s)
    const double Key = 0.18;          // gris moyen visé

    static readonly float[] HalfToFloat = BuildHalfTable();
    readonly byte[] _toSrgb = new byte[4096];
    double _lutGamma = double.NaN;
    double _logExposure = double.NaN;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastTime;

    static float[] BuildHalfTable()
    {
        var table = new float[65536];
        for (int i = 0; i < table.Length; i++)
        {
            float v = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            table[i] = float.IsFinite(v) && v > 0 ? v : 0;
        }
        return table;
    }

    void BuildSrgb(double gamma)
    {
        if (gamma == _lutGamma)
            return;
        for (int i = 0; i < _toSrgb.Length; i++)
        {
            double v = Math.Pow(i / (double)(_toSrgb.Length - 1), 1 / gamma);
            double s = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            _toSrgb[i] = (byte)Math.Round(Math.Clamp(s, 0, 1) * 255);
        }
        _lutGamma = gamma;
    }

    /// <summary>Exposition actuelle (pour l'affichage d'état).</summary>
    public double Exposure => double.IsNaN(_logExposure) ? 1 : Math.Exp(_logExposure);

    public unsafe void Process(IntPtr source, int pitch, Bitmap bmp, bool mirror, double compensation, double gamma)
    {
        int w = bmp.Width, h = bmp.Height;
        BuildSrgb(Math.Clamp(gamma, 0.2, 5));
        var half = HalfToFloat;

        // 1. Luminance moyenne (moyenne géométrique sur un pixel sur 8 dans chaque direction).
        double sumLog = 0;
        int samples = 0;
        for (int y = 0; y < h; y += 8)
        {
            ushort* row = (ushort*)((byte*)source + (long)y * pitch);
            for (int x = 0; x < w; x += 8)
            {
                ushort* p = row + x * 4;
                double lum = 0.2126 * half[p[0]] + 0.7152 * half[p[1]] + 0.0722 * half[p[2]];
                sumLog += Math.Log(1e-4 + lum);
                samples++;
            }
        }
        double target = Math.Log(Key) - (samples > 0 ? sumLog / samples : 0) + Math.Log(Math.Clamp(compensation, 0.1, 8));
        target = Math.Clamp(target, Math.Log(0.02), Math.Log(50));

        // 2. Adaptation progressive (pas de pompage d'une image à l'autre).
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 1);
        _lastTime = now;
        _logExposure = double.IsNaN(_logExposure) ? target : _logExposure + (target - _logExposure) * (1 - Math.Exp(-dt / Adaptation));
        float exposure = (float)Math.Exp(_logExposure);

        // 3. Courbe filmique ACES (approximation de Narkowicz) puis sRGB.
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            fixed (byte* lut = _toSrgb)
            {
                int last = _toSrgb.Length - 1;
                for (int y = 0; y < h; y++)
                {
                    ushort* src = (ushort*)((byte*)source + (long)y * pitch);
                    uint* dst = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        ushort* p = src + x * 4;
                        uint r = lut[(int)(Aces(half[p[0]] * exposure) * last)];
                        uint g = lut[(int)(Aces(half[p[1]] * exposure) * last)];
                        uint b = lut[(int)(Aces(half[p[2]] * exposure) * last)];
                        dst[mirror ? w - 1 - x : x] = (r << 16) | (g << 8) | b;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    static float Aces(float x)
    {
        float v = x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f);
        return v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
