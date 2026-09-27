using System.Diagnostics;

namespace AviXMirror.Radar;

/// <summary>
/// Spotter à LEDs : calcule, 20 fois par seconde, la couleur de chaque LED des deux barrettes
/// (voiture qui arrive = jaune → orange de plus en plus rempli ; voiture à côté = rouge ;
/// pris en sandwich = rouge clignotant). Fonctionne dans tous les modes (radar, caméra, capture).
/// </summary>
public sealed class Spotter : IDisposable
{
    const double OverlapHalfLength = 5.0;   // centres à moins de 5 m l'un de l'autre = voitures côte à côte
    const double MinLateral = 1.2, MaxLateral = 9.0;

    readonly IRadarTelemetry[] _sources = { new Rf2Telemetry(), new AcTelemetry() };
    readonly Thread _thread;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    volatile bool _running = true;
    volatile Settings _settings;
    Color[] _leds = Array.Empty<Color>();

    /// <summary>Nouvelle chaîne de couleurs, dans l'ordre de câblage.</summary>
    public event Action<Color[]>? Changed;

    /// <summary>Couleurs actuelles par côté (pour l'aperçu), de l'arrière vers l'avant.</summary>
    public (Color[] Left, Color[] Right) Sides { get; private set; } = (Array.Empty<Color>(), Array.Empty<Color>());

    public Spotter(Settings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Spotter" };
        _thread.Start();
    }

    public void UpdateSettings(Settings settings) => _settings = settings;

    void Run()
    {
        while (_running)
        {
            try
            {
                Step(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Spotter : " + ex.Message);
            }
            Thread.Sleep(50);
        }
    }

    void Step(Settings s)
    {
        int n = Math.Clamp(s.LedsPerSide, 1, 64);
        var (left, right) = Compute(s, n);
        Sides = (left, right);

        // Chaîne physique : droite puis gauche (ou l'inverse), chaque barrette éventuellement retournée.
        var r = s.LedInvertRight ? right.Reverse().ToArray() : right;
        var l = s.LedInvertLeft ? left.Reverse().ToArray() : left;
        var chain = s.LedOrder == LedChainOrder.DroiteGauche ? r.Concat(l).ToArray() : l.Concat(r).ToArray();

        float k = Math.Clamp(s.LedBrightness, 0, 255) / 255f;
        for (int i = 0; i < chain.Length; i++)
            chain[i] = Color.FromArgb((int)(chain[i].R * k), (int)(chain[i].G * k), (int)(chain[i].B * k));

        if (!chain.SequenceEqual(_leds))
        {
            _leds = chain;
            Changed?.Invoke(chain);
        }
    }

    (Color[] Left, Color[] Right) Compute(Settings s, int n)
    {
        var left = new Color[n];
        var right = new Color[n];
        Array.Fill(left, Color.Black);
        Array.Fill(right, Color.Black);

        var world = ReadWorld(s);
        bool blinkOn = (_clock.ElapsedMilliseconds / 125) % 2 == 0; // 4 Hz
        return world == null ? (left, right) : ComputeSides(world, s, n, blinkOn);
    }

    /// <summary>Couleurs de chaque côté (de l'arrière vers l'avant) pour un état de course donné.</summary>
    public static (Color[] Left, Color[] Right) ComputeSides(RadarWorld world, Settings s, int n, bool blinkOn)
    {
        var left = new Color[n];
        var right = new Color[n];
        Array.Fill(left, Color.Black);
        Array.Fill(right, Color.Black);
        var player = world.Player;
        if (player?.Orientation == null)
            return (left, right);

        var ori = player.Orientation;
        bool overlapLeft = false, overlapRight = false;
        double approachLeft = 0, approachRight = 0;
        double warn = Math.Max(OverlapHalfLength + 1, s.LedWarnDistance);

        foreach (var v in world.Vehicles)
        {
            if (v == player || v.InPits)
                continue;
            double dx = v.Position.X - player.Position.X, dy = v.Position.Y - player.Position.Y, dz = v.Position.Z - player.Position.Z;
            // Repère local du joueur : x = gauche, y = haut, z = arrière.
            double lx = ori[0].X * dx + ori[1].X * dy + ori[2].X * dz;
            double ly = ori[0].Y * dx + ori[1].Y * dy + ori[2].Y * dz;
            double lz = ori[0].Z * dx + ori[1].Z * dy + ori[2].Z * dz;
            if (world.InvertLateral)
                lx = -lx;
            if (Math.Abs(ly) > 15 || Math.Abs(lx) < MinLateral || Math.Abs(lx) > MaxLateral)
                continue;

            bool isLeft = lx > 0;
            if (Math.Abs(lz) <= OverlapHalfLength)
            {
                if (isLeft) overlapLeft = true; else overlapRight = true;
            }
            else if (lz > OverlapHalfLength && lz < warn)
            {
                double level = 1 - (lz - OverlapHalfLength) / (warn - OverlapHalfLength);
                if (isLeft) approachLeft = Math.Max(approachLeft, level); else approachRight = Math.Max(approachRight, level);
            }
        }

        bool sandwich = overlapLeft && overlapRight;
        Fill(left, overlapLeft, approachLeft, sandwich, blinkOn);
        Fill(right, overlapRight, approachRight, sandwich, blinkOn);
        return (left, right);
    }

    static void Fill(Color[] side, bool overlap, double approach, bool sandwich, bool blinkOn)
    {
        int n = side.Length;
        if (overlap)
        {
            var red = sandwich && !blinkOn ? Color.Black : Color.FromArgb(255, 0, 0);
            Array.Fill(side, red);
            return;
        }
        if (approach <= 0)
            return;

        // Remplissage de l'arrière vers l'avant, du jaune vers l'orange quand la voiture se rapproche.
        int lit = Math.Clamp((int)Math.Ceiling(approach * n), 1, n);
        var color = Color.FromArgb(255, (int)(200 - 110 * approach), 0);
        for (int i = 0; i < lit; i++)
            side[i] = color;
    }

    RadarWorld? ReadWorld(Settings s)
    {
        IEnumerable<IRadarTelemetry> candidates = s.RadarGame switch
        {
            RadarGame.LeMansUltimate => _sources.OfType<Rf2Telemetry>(),
            RadarGame.AssettoCorsa => _sources.OfType<AcTelemetry>(),
            _ => _sources,
        };
        foreach (var source in candidates)
            if (source.TryRead(s, out var world, out _) && world != null)
                return world;
        return null;
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(1000);
        foreach (var source in _sources)
            source.Dispose();
    }
}
