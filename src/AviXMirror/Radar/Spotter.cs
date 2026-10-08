using System.Diagnostics;

namespace AviXMirror.Radar;

/// <summary>
/// Spotter à LEDs : calcule, 20 fois par seconde, la couleur de chaque LED des deux barrettes
/// (voiture qui arrive = barrette de plus en plus remplie, jaune → orange ou couleur de sa catégorie ;
/// voiture à côté = rouge ou couleur de sa catégorie ; pris en sandwich = rouge clignotant ;
/// dive bomb = clignotement rapide). Fonctionne dans tous les modes (radar, caméra, capture).
/// </summary>
public sealed class Spotter : IDisposable
{
    const double OverlapHalfLength = 5.0;   // centres à moins de 5 m l'un de l'autre = voitures côte à côte
    const double MinLateral = 1.2, MaxLateral = 9.0;

    readonly TelemetrySet _telemetry = new();
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

    /// <summary>Vrai quand le joueur n'est pas au volant (pause, bureau) : LEDs éteintes.</summary>
    public bool Suspended { get; set; }

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

        var world = Suspended ? null : ReadWorld(s);
        long ms = _clock.ElapsedMilliseconds;
        bool blinkOn = (ms / 125) % 2 == 0;     // 4 Hz : sandwich
        bool fastOn = (ms / 50) % 2 == 0;       // 10 Hz : dive bomb
        return world == null ? (left, right) : ComputeSides(world, s, n, blinkOn, fastOn);
    }

    sealed class SideState
    {
        public bool Overlap, DiveBomb;
        public CarKind OverlapKind = CarKind.Other;
        public string OverlapClass = "";
        public double Approach;
        public CarKind ApproachKind = CarKind.Other;
        public string ApproachClass = "";
    }

    /// <summary>Couleurs de chaque côté (de l'arrière vers l'avant) pour un état de course donné.</summary>
    public static (Color[] Left, Color[] Right) ComputeSides(RadarWorld world, Settings s, int n, bool blinkOn, bool fastOn)
    {
        var left = new Color[n];
        var right = new Color[n];
        Array.Fill(left, Color.Black);
        Array.Fill(right, Color.Black);
        var player = world.Player;
        if (player?.Orientation == null)
            return (left, right);

        var ori = player.Orientation;
        var pv = player.Velocity;
        var sides = (Left: new SideState(), Right: new SideState());
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
            if (Math.Abs(ly) > 15 || Math.Abs(lx) > MaxLateral)
                continue;

            var side = lx > 0 ? sides.Left : sides.Right;
            var kind = CarClasses.Classify(v.Class);

            // Dive bomb : voiture qui arrive très vite de derrière, déjà décalée d'un côté, et qui sera
            // à notre hauteur dans moins de « LedDiveBombTime » secondes.
            if (s.LedDiveBomb && lz > OverlapHalfLength && lz < 40 && Math.Abs(lx) >= 0.5)
            {
                var vv = v.Velocity;
                double closing = -(ori[0].Z * (vv.X - pv.X) + ori[1].Z * (vv.Y - pv.Y) + ori[2].Z * (vv.Z - pv.Z));
                if (closing * 3.6 >= s.LedDiveBombSpeed && (lz - OverlapHalfLength) / closing <= s.LedDiveBombTime)
                    side.DiveBomb = true;
            }

            if (Math.Abs(lx) < MinLateral)
                continue;
            if (Math.Abs(lz) <= OverlapHalfLength)
            {
                if (!side.Overlap)
                {
                    side.OverlapKind = kind;
                    side.OverlapClass = v.Class;
                }
                side.Overlap = true;
            }
            else if (lz > OverlapHalfLength && lz < warn)
            {
                double level = 1 - (lz - OverlapHalfLength) / (warn - OverlapHalfLength);
                if (level > side.Approach)
                {
                    side.Approach = level;
                    side.ApproachKind = kind;
                    side.ApproachClass = v.Class;
                }
            }
        }

        bool sandwich = sides.Left.Overlap && sides.Right.Overlap;
        Fill(left, sides.Left, s, sandwich, blinkOn, fastOn);
        Fill(right, sides.Right, s, sandwich, blinkOn, fastOn);
        return (left, right);
    }

    static void Fill(Color[] leds, SideState side, Settings s, bool sandwich, bool blinkOn, bool fastOn)
    {
        var red = Color.FromArgb(255, 0, 0);
        if (sandwich)
        {
            Array.Fill(leds, blinkOn ? red : Color.Black);
            return;
        }
        if (side.Overlap)
        {
            Array.Fill(leds, s.LedClassColors ? ClassColor(s, side.OverlapKind, side.OverlapClass) : red);
            return;
        }
        if (side.DiveBomb)
        {
            Array.Fill(leds, fastOn ? ParseColor(s.LedDiveBombColor, red) : Color.Black);
            return;
        }
        if (side.Approach <= 0)
            return;

        // Remplissage de l'arrière vers l'avant quand la voiture se rapproche.
        int n = leds.Length;
        int lit = Math.Clamp((int)Math.Ceiling(side.Approach * n), 1, n);
        var color = s.LedClassColors
            ? ClassColor(s, side.ApproachKind, side.ApproachClass)
            : Color.FromArgb(255, (int)(200 - 110 * side.Approach), 0);
        for (int i = 0; i < lit; i++)
            leds[i] = color;
    }

    /// <summary>
    /// Couleur des LEDs pour une voiture : catégorie personnalisée (nom exact donné par le jeu) en priorité,
    /// sinon couleur de sa famille (réglables dans l'onglet LEDs).
    /// </summary>
    public static Color ClassColor(Settings s, CarKind kind, string? className)
    {
        var name = className?.Trim();
        if (!string.IsNullOrEmpty(name))
            foreach (var custom in s.LedCustomClasses)
                if (string.Equals(custom.Class?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return ParseColor(custom.Color, ClassColor(s, kind));
        return ClassColor(s, kind);
    }

    /// <summary>Couleur des LEDs pour une famille de voitures.</summary>
    public static Color ClassColor(Settings s, CarKind kind) => kind switch
    {
        CarKind.Hypercar => ParseColor(s.LedColorHypercar, Color.Red),
        CarKind.Lmp2 => ParseColor(s.LedColorLmp2, Color.Blue),
        CarKind.Lmp3 => ParseColor(s.LedColorLmp3, Color.Purple),
        CarKind.Gte => ParseColor(s.LedColorGte, Color.Orange),
        CarKind.Gt3 => ParseColor(s.LedColorGt3, Color.Lime),
        _ => ParseColor(s.LedColorOther, Color.Yellow),
    };

    static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex.Trim());
        }
        catch
        {
            return fallback;
        }
    }

    RadarWorld? ReadWorld(Settings s) => _telemetry.Read(s, out _);

    public void Dispose()
    {
        _running = false;
        _thread.Join(1000);
        _telemetry.Dispose();
    }
}
