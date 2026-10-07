namespace AviXMirror.Hud;

/// <summary>
/// Point de vue de l'ATH : projection du repère local du joueur (x = gauche, y = haut, z = arrière, en m)
/// vers l'image. <see cref="Cx"/>/<see cref="Cy"/> = centre optique (pixels), <see cref="Focal"/> = focale
/// (pixels), <see cref="Back"/> = recul de la caméra, <see cref="Up"/> = hauteur de l'œil,
/// <see cref="Side"/> = +1 ou -1 (effet miroir).
/// </summary>
public readonly record struct HudProjection(double Cx, double Cy, double Focal, double Back, double Up, double Side)
{
    /// <summary>Point de vue du rétro virtuel capturé, d'après les réglages « Capture » de l'ATH.</summary>
    public static HudProjection ForCapture(Settings s, int w, int h) => new(
        w * Math.Clamp(s.HudCaptureCenter, 0, 100) / 100,
        h * Math.Clamp(s.HudCaptureHorizon, 0, 100) / 100,
        FocalFor(s.HudCaptureFov, h),
        -s.HudCaptureForward, // le rétro virtuel est devant le centre de la voiture
        s.HudCaptureHeight,
        s.HudInvertSide ? -1 : 1); // un rétro inverse gauche et droite (réglage pour inverser si besoin)

    public static double FocalFor(double vfovDeg, int h) => h / 2.0 / Math.Tan(Math.Clamp(vfovDeg, 2, 120) * Math.PI / 360);

    public static double FovFor(double focal, int h) => 2 * Math.Atan(h / 2.0 / focal) * 180 / Math.PI;

    public PointF? Project(double lx, double ly, double lz)
    {
        double dz = lz - Back;
        if (dz < 0.5)
            return null;
        return new PointF((float)(Cx + Side * lx * Focal / dz), (float)(Cy - (ly - Up) * Focal / dz));
    }

    /// <summary>Point de l'ATH ancré sur une voiture : pointe de la flèche, au-dessus du toit (voir HudRenderer).</summary>
    public PointF? Anchor(HudTarget t) => Project(t.Lx, t.Ly + HudRenderer.ArrowHeight, t.Lz - HudRenderer.CarLength / 2);

    /// <summary>
    /// Ajuste le point de vue pour que l'ancre de chaque voiture tombe là où l'utilisateur l'a posée.
    /// Un point : seul le centre (horizon et axe) bouge. Deux points ou plus, assez écartés dans l'image :
    /// la focale (champ de vision) est aussi calculée, par moindres carrés.
    /// </summary>
    public HudProjection Fit(IReadOnlyList<(HudTarget Target, PointF Point)> matches, double minFocal, double maxFocal)
    {
        var rows = new List<(double U, double V, double X, double Y)>();
        foreach (var (t, p) in matches)
        {
            double dz = t.Lz - HudRenderer.CarLength / 2 - Back;
            if (dz < 0.5)
                continue;
            rows.Add((Side * t.Lx / dz, (t.Ly + HudRenderer.ArrowHeight - Up) / dz, p.X, p.Y));
        }
        if (rows.Count == 0)
            return this;

        int n = rows.Count;
        double su = rows.Sum(r => r.U), sv = rows.Sum(r => r.V);
        double sx = rows.Sum(r => r.X), sy = rows.Sum(r => r.Y);
        double focal = Focal;
        if (n >= 2)
        {
            double den = rows.Sum(r => r.U * r.U + r.V * r.V) - (su * su + sv * sv) / n;
            double num = rows.Sum(r => r.U * r.X - r.V * r.Y) - su * sx / n + sv * sy / n;
            // Voitures trop proches l'une de l'autre dans l'image : la focale serait imprécise, on la garde.
            double spreadPixels = Focal * Math.Sqrt(Math.Max(0, den) / n);
            if (den > 1e-12 && spreadPixels > 12)
            {
                double fitted = num / den;
                if (fitted >= minFocal && fitted <= maxFocal)
                    focal = fitted;
            }
        }
        double cx = (sx - focal * su) / n;
        double cy = (sy + focal * sv) / n;
        return this with { Cx = cx, Cy = cy, Focal = focal };
    }
}
