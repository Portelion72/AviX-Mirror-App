using System.Diagnostics;
using AviXMirror.Radar;

namespace AviXMirror.Hud;

/// <summary>
/// ATH des modes Caméra AC et Capture LMU : lit la télémétrie (lissée), place les voitures dans
/// l'image avec le même point de vue que la caméra, et dessine l'ATH par-dessus chaque image.
/// </summary>
public sealed class HudOverlay : IDisposable
{
    readonly TelemetrySet _telemetry = new();
    readonly MotionSmoother _smoother = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly object _lock = new();
    volatile Settings _settings;
    readonly MirrorMode _mode;
    double _lastTime = double.NaN;

    public HudOverlay(Settings settings, MirrorMode mode)
    {
        _settings = settings;
        _mode = mode;
    }

    public void UpdateSettings(Settings settings) => _settings = settings;

    /// <summary>Dessine l'ATH sur l'image (appelé par le tampon d'image après chaque nouvelle image).</summary>
    public void Draw(Bitmap bmp)
    {
        var s = _settings;
        if (!s.HudEnabled)
            return;
        lock (_lock)
        {
            var targets = Targets(s, out double speed);
            if (targets == null)
                return;
            var camera = Camera(s, bmp.Width, bmp.Height);
            using var g = Graphics.FromImage(bmp);
            HudRenderer.Draw(g, bmp.Width, bmp.Height, camera, -0.35, targets, speed, s);
            if (_mode == MirrorMode.Capture && s.HudCaptureGuides)
                DrawGuides(g, bmp.Width, bmp.Height, camera, s);
        }
    }

    /// <summary>Voitures derrière, à cet instant (pour l'outil « Aligner les flèches »), ou null sans télémétrie.</summary>
    public List<HudTarget>? CurrentTargets()
    {
        lock (_lock)
            return Targets(_settings, out _);
    }

    /// <summary>Voitures derrière le joueur, dans son repère local (positions lissées).</summary>
    List<HudTarget>? Targets(Settings s, out double speed)
    {
        speed = 0;
        var world = Read(s);
        var player = world?.Player;
        if (world == null || player?.Orientation == null)
            return null;

        double now = _clock.Elapsed.TotalSeconds;
        if (world.Time != _lastTime)
        {
            _lastTime = world.Time;
            _smoother.OnSample(world, now);
        }

        var ori = _smoother.Orientation(player, now) ?? player.Orientation;
        var pp = _smoother.Position(player, now);
        var pv = player.Velocity;
        speed = Math.Sqrt(pv.X * pv.X + pv.Y * pv.Y + pv.Z * pv.Z);

        var targets = new List<HudTarget>();
        foreach (var v in world.Vehicles)
        {
            if (v == player || v.InPits)
                continue;
            var vp = _smoother.Position(v, now);
            double dx = vp.X - pp.X, dy = vp.Y - pp.Y, dz = vp.Z - pp.Z;
            double lx = ori[0].X * dx + ori[1].X * dy + ori[2].X * dz;
            double ly = ori[0].Y * dx + ori[1].Y * dy + ori[2].Y * dz;
            double lz = ori[0].Z * dx + ori[1].Z * dy + ori[2].Z * dz;
            if (world.InvertLateral)
                lx = -lx;
            // Autre portion de piste (pont, ligne droite parallèle) : ignorée.
            if (Math.Abs(ly) > 15 || Math.Abs(lx) > 20)
                continue;
            var rv = v.Velocity;
            // Voiture roulant dans l'autre sens (portion de piste en sens inverse) : ignorée.
            double vForward = ori[0].Z * rv.X + ori[1].Z * rv.Y + ori[2].Z * rv.Z;
            double pForward = ori[0].Z * pv.X + ori[1].Z * pv.Y + ori[2].Z * pv.Z;
            if (speed > 5 && vForward * pForward < 0 && Math.Abs(vForward) > 5)
                continue;
            double relZ = ori[0].Z * (rv.X - pv.X) + ori[1].Z * (rv.Y - pv.Y) + ori[2].Z * (rv.Z - pv.Z);
            targets.Add(new HudTarget(lx, ly, lz, -relZ * 3.6));
        }
        return targets;
    }

    /// <summary>
    /// Repères de réglage (capture) : ligne d'horizon et marques au sol à 10, 20, 40 et 80 m, au centre
    /// et sur les bords d'une voie, pour aligner le point de vue de l'ATH sur l'image du rétro.
    /// </summary>
    static void DrawGuides(Graphics g, int w, int h, Func<double, double, double, PointF?> camera, Settings s)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float unit = h / 400f;
        float horizon = (float)(h * Math.Clamp(s.HudCaptureHorizon, 0, 100) / 100);
        using var pen = new Pen(Color.FromArgb(230, 255, 0, 200), 2 * unit) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawLine(pen, 0, horizon, w, horizon);
        using var font = new Font(Ui.Theme.FontName, 13 * unit, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(255, 255, 0, 200));
        g.DrawString("HORIZON", font, brush, 6 * unit, horizon - 18 * unit);

        const double ground = -0.35, lane = 1.8;
        PointF? previousLeft = null, previousRight = null;
        foreach (double d in new[] { 10.0, 20.0, 40.0, 80.0 })
        {
            var left = camera(lane, ground, d);
            var right = camera(-lane, ground, d);
            var center = camera(0, ground, d);
            if (left is { } l && right is { } r)
            {
                g.DrawLine(pen, l, r);
                if (previousLeft is { } pl && previousRight is { } pr)
                {
                    g.DrawLine(pen, pl, l);
                    g.DrawLine(pen, pr, r);
                }
                previousLeft = l;
                previousRight = r;
            }
            if (center is { } c)
                g.DrawString($"{d:0} m", font, brush, c.X + 4 * unit, c.Y - 16 * unit);
        }
    }

    /// <summary>
    /// Projection du repère local du joueur vers l'image, avec le point de vue de la caméra :
    /// caméra AC = réglages exacts de la caméra arrière ; capture = point de vue du rétro virtuel du jeu
    /// (réglages « Capture » de l'ATH, propres à chaque jeu, ajustables avec « Aligner les flèches »).
    /// </summary>
    Func<double, double, double, PointF?> Camera(Settings s, int w, int h)
    {
        if (_mode != MirrorMode.CameraAssettoCorsa)
            return HudProjection.ForCapture(s, w, h).Project;

        double width = Math.Clamp(s.AcCamResWidth, 64, 2048), height = Math.Clamp(s.AcCamResHeight, 32, 2048);
        double hfov = Math.Clamp(s.AcCamFov, 10, 150) * Math.PI / 180;
        double vfovDeg = 2 * Math.Atan(Math.Tan(hfov / 2) * height / width) * 180 / Math.PI;
        // Côté de l'image : symétrique du repère de la voiture (un rétro inverse gauche et droite),
        // avec un réglage pour inverser si besoin.
        double side = (s.AcCamMirror ? 1 : -1) * (s.HudInvertSide ? -1 : 1);
        return new HudProjection(w / 2.0, h / 2.0, HudProjection.FocalFor(vfovDeg, h), s.AcCamBack, s.AcCamUp, side).Project;
    }

    // La caméra AC ne concerne qu'Assetto Corsa ; la capture suit le réglage « Jeu ».
    RadarWorld? Read(Settings s) => _mode == MirrorMode.CameraAssettoCorsa
        ? _telemetry.For(Games.Get(RadarGame.AssettoCorsa)).ReadOrNull(s)
        : _telemetry.Read(s, out _);

    public void Dispose()
    {
        lock (_lock)
            _telemetry.Dispose();
    }
}
