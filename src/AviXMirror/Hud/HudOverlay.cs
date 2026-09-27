using System.Diagnostics;
using AviXMirror.Radar;

namespace AviXMirror.Hud;

/// <summary>
/// ATH des modes Caméra AC et Capture LMU : lit la télémétrie (lissée), place les voitures dans
/// l'image avec le même point de vue que la caméra, et dessine l'ATH par-dessus chaque image.
/// </summary>
public sealed class HudOverlay : IDisposable
{
    readonly IRadarTelemetry[] _sources = { new Rf2Telemetry(), new AcTelemetry() };
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
            var world = Read(s);
            var player = world?.Player;
            if (world == null || player?.Orientation == null)
                return;

            double now = _clock.Elapsed.TotalSeconds;
            if (world.Time != _lastTime)
            {
                _lastTime = world.Time;
                _smoother.OnSample(world, now);
            }

            var ori = _smoother.Orientation(player, now) ?? player.Orientation;
            var pp = _smoother.Position(player, now);
            var pv = player.Velocity;
            double speed = Math.Sqrt(pv.X * pv.X + pv.Y * pv.Y + pv.Z * pv.Z);

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
                if (Math.Abs(ly) > 15)
                    continue;
                var rv = v.Velocity;
                double relZ = ori[0].Z * (rv.X - pv.X) + ori[1].Z * (rv.Y - pv.Y) + ori[2].Z * (rv.Z - pv.Z);
                targets.Add(new HudTarget(lx, ly, lz, -relZ * 3.6));
            }

            var camera = Camera(s, bmp.Width, bmp.Height);
            using var g = Graphics.FromImage(bmp);
            HudRenderer.Draw(g, bmp.Width, bmp.Height, camera, -0.35, targets, speed, s);
        }
    }

    /// <summary>
    /// Projection du repère local du joueur vers l'image, avec le point de vue de la caméra :
    /// caméra AC = réglages exacts de la caméra arrière ; capture LMU = point de vue approché du rétro virtuel.
    /// </summary>
    Func<double, double, double, PointF?> Camera(Settings s, int w, int h)
    {
        double back, up, vfovDeg;
        bool mirror;
        if (_mode == MirrorMode.CameraAssettoCorsa)
        {
            back = s.AcCamBack;
            up = s.AcCamUp;
            double width = Math.Clamp(s.AcCamResWidth, 64, 2048), height = Math.Clamp(s.AcCamResHeight, 32, 2048);
            double hfov = Math.Clamp(s.AcCamFov, 10, 150) * Math.PI / 180;
            vfovDeg = 2 * Math.Atan(Math.Tan(hfov / 2) * height / width) * 180 / Math.PI;
            mirror = s.AcCamMirror;
        }
        else
        {
            back = -s.HudCaptureForward; // le rétro virtuel est devant le centre de la voiture
            up = s.HudCaptureHeight;
            vfovDeg = s.HudCaptureFov;
            mirror = true;
        }

        double focal = h / 2.0 / Math.Tan(Math.Clamp(vfovDeg, 2, 120) * Math.PI / 360);
        return (lx, ly, lz) =>
        {
            double dz = lz - back;
            if (dz < 0.5)
                return null;
            double x = w / 2.0 + (mirror ? -1 : 1) * lx * focal / dz;
            double y = h / 2.0 - (ly - up) * focal / dz;
            return new PointF((float)x, (float)y);
        };
    }

    RadarWorld? Read(Settings s)
    {
        IEnumerable<IRadarTelemetry> candidates = s.RadarGame switch
        {
            RadarGame.LeMansUltimate => _sources.OfType<Rf2Telemetry>(),
            RadarGame.AssettoCorsa => _sources.OfType<AcTelemetry>(),
            _ => _mode == MirrorMode.CameraAssettoCorsa ? _sources.OfType<AcTelemetry>() : _sources,
        };
        foreach (var source in candidates)
            if (source.TryRead(s, out var world, out _) && world != null)
                return world;
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
            foreach (var source in _sources)
                source.Dispose();
    }
}
