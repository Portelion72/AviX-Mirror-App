using System.Drawing.Imaging;

namespace AviXMirror.Hud;

/// <summary>
/// Calibration automatique des flèches (mode Capture) : pendant qu'on roule avec des voitures derrière, on garde
/// des images du rétro et la position des voitures (télémétrie). On cherche ensuite le point de vue (horizon,
/// centre, champ de vision) pour lequel le cadre projeté de chaque voiture tombe sur une zone détaillée de l'image
/// (carrosserie, vitres, phares) plutôt que sur le fond uni (route, ciel) qui l'entoure.
/// Score d'un cadre = détail moyen dedans − détail moyen autour ; somme sur toutes les voitures de toutes les images.
/// </summary>
public sealed class HudAutoCalibrator
{
    const double CarWidth = 1.9, CarHeight = 1.2;
    const int MinBoxes = 10;

    sealed record Sample(double[] Integral, int Width, int Height, List<HudTarget> Targets);

    readonly List<Sample> _samples = new();

    public int SampleCount => _samples.Count;
    public int CarCount => _samples.Sum(s => s.Targets.Count);

    public void Clear() => _samples.Clear();

    /// <summary>Ajoute une image du rétro (taille de la capture) et les voitures derrière à cet instant.</summary>
    public void Add(Bitmap image, IEnumerable<HudTarget> targets)
    {
        var useful = targets.Where(t => t.Lz > 6 && t.Lz < 90 && Math.Abs(t.Lx) < 12).ToList();
        if (useful.Count == 0 || image.Width < 16 || image.Height < 8)
            return;
        _samples.Add(new Sample(DetailIntegral(image), image.Width, image.Height, useful));
    }

    /// <summary>Image intégrale du « détail » (gradient de luminance), normalisé par sa moyenne sur l'image.</summary>
    static unsafe double[] DetailIntegral(Bitmap image)
    {
        int w = image.Width, h = image.Height;
        var lum = new float[w * h];
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                uint* row = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                for (int x = 0; x < w; x++)
                {
                    uint c = row[x];
                    lum[y * w + x] = ((c >> 16) & 0xFF) * 0.3f + ((c >> 8) & 0xFF) * 0.59f + (c & 0xFF) * 0.11f;
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        var detail = new float[w * h];
        double total = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float d = (x > 0 ? Math.Abs(lum[i] - lum[i - 1]) : 0) + (y > 0 ? Math.Abs(lum[i] - lum[i - w]) : 0);
                detail[i] = d;
                total += d;
            }
        double mean = total / (w * h) + 1e-6;

        var integral = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double row = 0;
            for (int x = 0; x < w; x++)
            {
                row += detail[y * w + x] / mean;
                integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row;
            }
        }
        return integral;
    }

    static double BoxSum(double[] I, int w, int x0, int y0, int x1, int y1)
    {
        int s = w + 1;
        return I[y1 * s + x1] - I[y0 * s + x1] - I[y1 * s + x0] + I[y0 * s + x0];
    }

    double Score(double centerPct, double horizonPct, double fov, Settings s)
    {
        double total = 0;
        int boxes = 0;
        foreach (var sample in _samples)
        {
            int w = sample.Width, h = sample.Height;
            var p = new HudProjection(w * centerPct / 100, h * horizonPct / 100, HudProjection.FocalFor(fov, h),
                -s.HudCaptureForward, s.HudCaptureHeight, s.HudInvertSide ? -1 : 1);
            foreach (var t in sample.Targets)
            {
                double z = t.Lz - HudRenderer.CarLength / 2;
                if (p.Project(t.Lx + CarWidth / 2, t.Ly, z) is not { } a || p.Project(t.Lx - CarWidth / 2, t.Ly + CarHeight, z) is not { } b)
                    continue;
                double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
                double bw = x1 - x0, bh = y1 - y0;
                if (bw < 3 || bh < 2 || bw > w / 2.0)
                    continue;
                int ix0 = (int)x0, iy0 = (int)y0, ix1 = (int)x1 + 1, iy1 = (int)y1 + 1;
                int ox0 = (int)(x0 - bw), oy0 = (int)(y0 - bh), ox1 = (int)(x1 + bw) + 1, oy1 = (int)(y1 + bh) + 1;
                if (ox0 < 0 || oy0 < 0 || ox1 > w || oy1 > h)
                    continue;
                double inner = BoxSum(sample.Integral, w, ix0, iy0, ix1, iy1);
                int area = (ix1 - ix0) * (iy1 - iy0);
                double outer = BoxSum(sample.Integral, w, ox0, oy0, ox1, oy1) - inner;
                int outerArea = (ox1 - ox0) * (oy1 - oy0) - area;
                total += inner / area - outer / Math.Max(1, outerArea);
                boxes++;
            }
        }
        return boxes >= MinBoxes ? total / boxes : double.NegativeInfinity;
    }

    public sealed record Result(double Horizon, double Center, double Fov, double Confidence);

    /// <summary>
    /// Meilleur point de vue : recherche grossière (pas de 2) puis fine (pas de 0,5) ; null si trop peu de voitures.
    /// La confiance est l'écart du meilleur score à la moyenne, en écarts-types (au-delà de 3 : résultat net).
    /// </summary>
    public Result? Solve(Settings s)
    {
        if (CarCount < MinBoxes)
            return null;
        var scores = new List<double>();
        (double Score, double C, double H, double F) best = (double.NegativeInfinity, 50, 50, 14);
        for (double h = 10; h <= 90; h += 2)
            for (double c = 30; c <= 70; c += 2)
                for (double f = 6; f <= 40; f += 2)
                {
                    double sc = Score(c, h, f, s);
                    if (double.IsNegativeInfinity(sc))
                        continue;
                    scores.Add(sc);
                    if (sc > best.Score)
                        best = (sc, c, h, f);
                }
        if (double.IsNegativeInfinity(best.Score) || scores.Count < 10)
            return null;

        var coarse = best;
        for (double h = coarse.H - 2; h <= coarse.H + 2; h += 0.5)
            for (double c = coarse.C - 2; c <= coarse.C + 2; c += 0.5)
                for (double f = Math.Max(2, coarse.F - 2); f <= coarse.F + 2; f += 0.5)
                {
                    double sc = Score(c, h, f, s);
                    if (sc > best.Score)
                        best = (sc, c, h, f);
                }

        double mean = scores.Average();
        double std = Math.Sqrt(scores.Sum(v => (v - mean) * (v - mean)) / scores.Count) + 1e-9;
        return new Result(best.H, best.C, best.F, (best.Score - mean) / std);
    }
}
