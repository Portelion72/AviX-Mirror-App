using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AviXMirror.Util;

namespace AviXMirror.Radar;

/// <summary>
/// Rétroviseur synthétique : dessine en perspective les voitures situées derrière le joueur
/// à partir des positions fournies par le jeu (Le Mans Ultimate ou Assetto Corsa).
/// Aucun rendu du jeu n'est nécessaire, donc rien ne s'affiche sur l'écran principal.
/// </summary>
public sealed class RadarSource : IDisposable
{
    const double MaxExtrapolationSeconds = 0.4;

    readonly FrameBuffer _output;
    readonly IRadarTelemetry[] _sources = { new Rf2Telemetry(), new AcTelemetry() };
    IRadarTelemetry? _active;
    readonly TrackMap _track = new();
    readonly Thread _thread;
    volatile bool _running = true;
    volatile Settings _settings;

    RadarWorld? _world;
    bool _hasData;
    double _lastET = double.NaN;
    readonly Stopwatch _sinceUpdate = Stopwatch.StartNew();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly MotionSmoother _smoother = new();

    readonly Dictionary<int, Font> _fonts = new();

    /// <summary>Taille de rendu quand les réglages indiquent 0 (taille de la fenêtre).</summary>
    public Size FallbackSize { get; init; } = new(1280, 400);

    public string Status { get; private set; } = "Démarrage…";

    public RadarSource(FrameBuffer output, Settings settings)
    {
        _output = output;
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Radar" };
        _thread.Start();
    }

    public void UpdateSettings(Settings settings) => _settings = settings;

    void Run()
    {
        var clock = Stopwatch.StartNew();
        while (_running)
        {
            var s = _settings;
            double frameTime = 1.0 / Math.Clamp(s.TargetFps, 5, 240);
            double start = clock.Elapsed.TotalSeconds;

            // Lecture à chaque image : AC envoie ses données à chaque image du jeu, LMU ~5 fois par seconde.
            Poll();

            int w = s.RadarWidth > 0 ? s.RadarWidth : FallbackSize.Width;
            int h = s.RadarHeight > 0 ? s.RadarHeight : FallbackSize.Height;
            try
            {
                _output.Write(Math.Max(64, w), Math.Max(32, h), bmp =>
                {
                    using var g = Graphics.FromImage(bmp);
                    Render(g, bmp.Width, bmp.Height, s);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Radar : " + ex.Message);
            }

            double remaining = frameTime - (clock.Elapsed.TotalSeconds - start);
            if (remaining > 0)
                Thread.Sleep(TimeSpan.FromSeconds(remaining));
        }
    }

    void Poll()
    {
        var s = _settings;
        var candidates = s.RadarGame switch
        {
            RadarGame.LeMansUltimate => _sources.OfType<Rf2Telemetry>().Cast<IRadarTelemetry>(),
            RadarGame.AssettoCorsa => _sources.OfType<AcTelemetry>(),
            // Auto : on garde le jeu déjà trouvé, sinon on essaie les autres.
            _ => _active != null ? _sources.OrderBy(x => x == _active ? 0 : 1) : _sources,
        };

        var waiting = new List<string>();
        foreach (var source in candidates)
        {
            if (source.TryRead(s, out var world, out var status) && world != null)
            {
                _active = source;
                if (world.Time != _lastET)
                {
                    _lastET = world.Time;
                    _sinceUpdate.Restart();
                    _smoother.OnSample(world, _clock.Elapsed.TotalSeconds);
                    _track.Update(world);
                }
                _world = world;
                _hasData = true;
                Status = world.Vehicles.Count == 0
                    ? status + " — pas de session en cours."
                    : $"{status} — tracé du circuit connu à {_track.Coverage:P0}.";
                return;
            }
            waiting.Add(status);
        }

        _active = null;
        _hasData = false;
        Status = string.Join("\n", waiting);
    }

    struct Car
    {
        public double Lx, Ly, Lz, Closing;
        public string Label, Class, Model;
        public bool Headlights;
    }

    static double Dot(in RF2Vec3 a, in RF2Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    void Render(Graphics g, int w, int h, Settings s)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float horizon = h * 0.42f;
        DrawBackground(g, w, h, horizon);

        var world = _world;
        if (!_hasData || world == null)
        {
            // Aucun jeu : page de veille AVIX.
            Ui.Splash.Draw(g, w, h, "En attente du jeu…");
            return;
        }
        if (world.Vehicles.Count == 0)
        {
            DrawCenteredText(g, w, h, Status);
            return;
        }

        var player = world.Player;
        if (player?.Orientation == null)
        {
            DrawCenteredText(g, w, h, "Pas de voiture joueur (spectateur ?)");
            return;
        }

        double dt = Math.Min(_sinceUpdate.Elapsed.TotalSeconds, MaxExtrapolationSeconds);
        double now = _clock.Elapsed.TotalSeconds;
        // Position et orientation lissées : prolongées entre deux relevés, sans à-coup à chaque nouveau relevé.
        var ori = _smoother.Orientation(player, now) ?? player.Orientation;
        var pv = player.Velocity;
        var pp = _smoother.Position(player, now);

        double fov = Math.Clamp(s.RadarFov, 20, 150) * Math.PI / 180;
        double focal = w / 2.0 / Math.Tan(fov / 2);
        var view = new View(ori, pp, world.InvertLateral, w, h, horizon, focal);
        // Vitesse vers l'avant = -z local.
        double forward = -(ori[0].Z * pv.X + ori[1].Z * pv.Y + ori[2].Z * pv.Z);
        double playerLapDist = player.LapDist + forward * dt;
        if (double.IsNaN(playerLapDist) || !DrawTrack(g, view, playerLapDist, s.RadarRange, KerbPalette.For(world.TrackName)))
            DrawStaticRoad(g, w, h, horizon);

        var behind = new List<Car>();
        bool warnLeft = false, warnRight = false;

        foreach (var v in world.Vehicles)
        {
            if (v == player)
                continue;

            var vv = v.Velocity;
            var vp = _smoother.Position(v, now);
            var (lx, ly, lz) = view.ToLocal(vp.X, vp.Y, vp.Z);

            if (Math.Abs(ly) > 15)
                continue; // Autre partie du circuit (pont, tunnel…).

            if (lz > -6 && lz < 3.5 && Math.Abs(lx) > 1.2 && Math.Abs(lx) < 8)
            {
                if (lx > 0) warnLeft = true; else warnRight = true;
                continue;
            }

            if (lz < 3.5 || lz > s.RadarRange || Math.Abs(lx) > lz * 1.5 + 5)
                continue;

            var rel = new RF2Vec3 { X = vv.X - pv.X, Y = vv.Y - pv.Y, Z = vv.Z - pv.Z };
            double relZ = ori[0].Z * rel.X + ori[1].Z * rel.Y + ori[2].Z * rel.Z;

            string place = v.Place > 0 ? $"P{v.Place}" : "";
            behind.Add(new Car
            {
                Lx = lx,
                Ly = ly,
                Lz = lz,
                Closing = -relZ * 3.6,
                Label = s.RadarShowNames ? $"{place} {ShortName(v.Name)}".Trim() : place,
                Class = v.Class,
                Model = s.RadarBrandFronts && world.Game == "Le Mans Ultimate" ? v.Model : "",
                Headlights = v.Headlights,
            });
        }

        foreach (var car in behind.OrderByDescending(c => c.Lz))
            DrawCar(g, w, horizon, focal, car, withLabel: !s.HudEnabled);

        // ATH façon caméra de recul : flèches, échelles de distance et de temps.
        var projector = view;
        Hud.HudRenderer.Draw(g, w, h, (x, y, z) => z < View.Near ? null : projector.Project((x, y, z)), 0,
            behind.Select(c => new Hud.HudTarget(c.Lx, c.Ly, c.Lz, c.Closing)),
            Math.Sqrt(pv.X * pv.X + pv.Y * pv.Y + pv.Z * pv.Z), s);

        if (warnLeft) DrawSideWarning(g, w, h, left: true);
        if (warnRight) DrawSideWarning(g, w, h, left: false);
    }

    static string ShortName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1].ToUpperInvariant() : name.ToUpperInvariant();
    }

    static void DrawBackground(Graphics g, int w, int h, float horizon)
    {
        using (var sky = new LinearGradientBrush(new RectangleF(0, 0, w, horizon + 1),
                   Color.FromArgb(20, 26, 36), Color.FromArgb(58, 70, 86), LinearGradientMode.Vertical))
            g.FillRectangle(sky, 0, 0, w, horizon + 1);

        using var ground = new LinearGradientBrush(new RectangleF(0, horizon, w, h - horizon + 1),
            Color.FromArgb(34, 44, 36), Color.FromArgb(16, 26, 18), LinearGradientMode.Vertical);
        g.FillRectangle(ground, 0, horizon, w, h - horizon);
    }

    /// <summary>Route droite de secours tant que le tracé du circuit n'est pas connu.</summary>
    static void DrawStaticRoad(Graphics g, int w, int h, float horizon)
    {
        var road = new[]
        {
            new PointF(w * 0.47f, horizon), new PointF(w * 0.53f, horizon),
            new PointF(w * 1.05f, h), new PointF(w * -0.05f, h),
        };
        using (var roadBrush = new SolidBrush(Color.FromArgb(52, 54, 58)))
            g.FillPolygon(roadBrush, road);
        using var line = new Pen(Color.FromArgb(110, 230, 230, 230), 2f);
        g.DrawLine(line, road[0], road[3]);
        g.DrawLine(line, road[1], road[2]);
    }

    /// <summary>Caméra du rétro : repère local du joueur et projection en perspective.</summary>
    readonly struct View
    {
        public const double Near = 1.2;
        readonly RF2Vec3[] _ori;
        readonly RF2Vec3 _eye;
        readonly bool _invert;
        public readonly int W, H;
        public readonly float Horizon;
        public readonly double Focal;

        public View(RF2Vec3[] ori, RF2Vec3 eye, bool invert, int w, int h, float horizon, double focal)
        {
            _ori = ori; _eye = eye; _invert = invert; W = w; H = h; Horizon = horizon; Focal = focal;
        }

        /// <summary>Monde -> repère local (x = gauche, y = haut, z = arrière).</summary>
        public (double X, double Y, double Z) ToLocal(double x, double y, double z)
        {
            double dx = x - _eye.X, dy = y - _eye.Y, dz = z - _eye.Z;
            double lx = _ori[0].X * dx + _ori[1].X * dy + _ori[2].X * dz;
            double ly = _ori[0].Y * dx + _ori[1].Y * dy + _ori[2].Y * dz;
            double lz = _ori[0].Z * dx + _ori[1].Z * dy + _ori[2].Z * dz;
            return (_invert ? -lx : lx, ly, lz);
        }

        /// <summary>Projette un point au sol (repère local) sur l'image du rétro.</summary>
        public PointF Project((double X, double Y, double Z) p)
        {
            double scale = Focal / Math.Max(p.Z, Near);
            return new PointF((float)(W / 2.0 - p.X * scale), (float)(Horizon - (p.Y - 0.8) * scale));
        }
    }

    struct RoadPoint
    {
        public bool Valid;
        public (double X, double Y, double Z) Left, Right, KerbLeft, KerbRight;
        public int Index;
        public double Heading;   // cap de la piste (rad), pour repérer les virages
        public bool Corner;      // vibreurs dessinés seulement dans les virages
    }

    /// <summary>
    /// Dessine la piste derrière le joueur en suivant le tracé appris : elle tourne dans les virages
    /// et suit le relief. Retourne faux si le tracé n'est pas encore connu à cet endroit.
    /// </summary>
    bool DrawTrack(Graphics g, in View view, double playerLapDist, double range, Color[] kerbColors)
    {
        if (!_track.HasData)
            return false;

        int steps = (int)((range + 40) / TrackMap.BinSize);
        var points = new RoadPoint[steps + 4];
        int valid = 0;
        for (int k = 0; k < points.Length; k++)
        {
            // On part un peu devant le joueur pour couvrir le bas de l'image.
            double lapDist = playerLapDist + (3 - k) * TrackMap.BinSize;
            if (!_track.TryGetPoint(lapDist, out var c, out var dir, out var hw))
                continue;
            double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            if (len < 1e-3)
                continue;
            // Perpendiculaire horizontale à la piste (le signe importe peu : les bords sont symétriques).
            double px = -dir.Z / len, pz = dir.X / len;
            points[k] = new RoadPoint
            {
                Valid = true,
                Left = view.ToLocal(c.X + px * hw, c.Y, c.Z + pz * hw),
                Right = view.ToLocal(c.X - px * hw, c.Y, c.Z - pz * hw),
                KerbLeft = view.ToLocal(c.X + px * (hw + 0.9), c.Y, c.Z + pz * (hw + 0.9)),
                KerbRight = view.ToLocal(c.X - px * (hw + 0.9), c.Y, c.Z - pz * (hw + 0.9)),
                Index = (int)Math.Floor(lapDist / TrackMap.BinSize),
                Heading = Math.Atan2(dir.X, dir.Z),
            };
            valid++;
        }
        if (valid < 6)
            return false;
        MarkCorners(points);

        var asphalt = Color.FromArgb(58, 60, 64);
        var fog = Color.FromArgb(58, 70, 86);
        using var brush = new SolidBrush(asphalt);
        var kerbBrushes = kerbColors.Select(c => new SolidBrush(c)).ToArray();
        using var line = new Pen(Color.FromArgb(220, 235, 235, 235), 1.5f);

        // Du plus loin au plus proche.
        for (int k = points.Length - 1; k > 0; k--)
        {
            ref readonly var far = ref points[k];
            ref readonly var near = ref points[k - 1];
            if (!far.Valid || !near.Valid)
                continue;
            if (!Clip(far.Left, near.Left, out var fl, out var nl) || !Clip(far.Right, near.Right, out var fr, out var nr))
                continue;

            double dist = Math.Max(0, Math.Min(fl.Z, fr.Z));
            double t = Math.Clamp(dist / (range + 40), 0, 1) * 0.7;
            brush.Color = Mix(asphalt, fog, t);

            var quad = new[] { view.Project(fl), view.Project(fr), view.Project(nr), view.Project(nl) };
            g.FillPolygon(brush, quad);

            // Vibreurs rouge/blanc sur les bords, lignes blanches de bord de piste.
            if (dist < 60 && far.Corner && Clip(far.KerbLeft, near.KerbLeft, out var fkl, out var nkl) &&
                Clip(far.KerbRight, near.KerbRight, out var fkr, out var nkr))
            {
                // Les couleurs se répètent tous les 2 m, comme les bandes d'un vrai vibreur.
                var kerb = kerbBrushes[((far.Index % kerbBrushes.Length) + kerbBrushes.Length) % kerbBrushes.Length];
                g.FillPolygon(kerb, new[] { view.Project(fkl), view.Project(fl), view.Project(nl), view.Project(nkl) });
                g.FillPolygon(kerb, new[] { view.Project(fr), view.Project(fkr), view.Project(nkr), view.Project(nr) });
            }
            line.Width = (float)Math.Clamp(view.Focal / Math.Max(dist, 1) * 0.12, 1, 4);
            g.DrawLine(line, quad[0], quad[3]);
            g.DrawLine(line, quad[1], quad[2]);
        }
        foreach (var b in kerbBrushes)
            b.Dispose();
        return true;
    }

    /// <summary>
    /// Repère les virages : la piste change de cap de plus de 5° sur 12 m (rayon inférieur à ~140 m).
    /// Les vibreurs sont prolongés de quelques mètres avant et après, comme sur un vrai circuit.
    /// </summary>
    static void MarkCorners(RoadPoint[] points)
    {
        const int Half = 3;          // 3 cases de 2 m de chaque côté
        const int Extend = 4;        // prolongement des vibreurs (8 m)
        const double Threshold = 5 * Math.PI / 180;
        var corner = new bool[points.Length];
        for (int k = Half; k < points.Length - Half; k++)
        {
            if (!points[k - Half].Valid || !points[k + Half].Valid)
                continue;
            double turn = Math.Abs(Math.IEEERemainder(points[k + Half].Heading - points[k - Half].Heading, 2 * Math.PI));
            if (turn > Threshold)
                for (int j = Math.Max(0, k - Extend); j <= Math.Min(points.Length - 1, k + Extend); j++)
                    corner[j] = true;
        }
        for (int k = 0; k < points.Length; k++)
            points[k].Corner = corner[k];
    }

    /// <summary>Coupe un segment au plan proche de la caméra (les points derrière elle ne se projettent pas).</summary>
    static bool Clip((double X, double Y, double Z) a, (double X, double Y, double Z) b,
        out (double X, double Y, double Z) ca, out (double X, double Y, double Z) cb)
    {
        ca = a; cb = b;
        const double near = View.Near;
        if (a.Z < near && b.Z < near)
            return false;
        if (a.Z < near)
            ca = Lerp(a, b, (near - a.Z) / (b.Z - a.Z));
        else if (b.Z < near)
            cb = Lerp(b, a, (near - b.Z) / (a.Z - b.Z));
        return true;
    }

    static (double X, double Y, double Z) Lerp((double X, double Y, double Z) a, (double X, double Y, double Z) b, double t) =>
        (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    enum CarKind { Hypercar, Lmp2, Lmp3, Gte, Gt3, Other }

    /// <summary>
    /// Catégorie à partir de la classe LMU (« Hyper », « LMP2 »…) ou de l'identifiant de voiture
    /// Assetto Corsa (« ks_ferrari_488_gt3 », « ks_porsche_919_hybrid_2016 »…).
    /// </summary>
    static CarKind Classify(string cls)
    {
        var c = cls.ToUpperInvariant();
        static bool Any(string text, params string[] keys) => keys.Any(text.Contains);
        if (Any(c, "HYPER", "LMH", "LMDH", "LMP1", "919", "TS050", "R18", "499P", "963", "9X8", "GR010", "V-SERIES", "VSERIES"))
            return CarKind.Hypercar;
        if (Any(c, "LMP2", "ORECA")) return CarKind.Lmp2;
        if (Any(c, "LMP3", "JS_P3", "JS P3")) return CarKind.Lmp3;
        if (Any(c, "GTE", "GTLM", "GT2")) return CarKind.Gte;
        if (Any(c, "GT3", "GTM", "GT4", "GT")) return CarKind.Gt3;
        return CarKind.Other;
    }

    static string KindName(CarKind kind) => kind switch
    {
        CarKind.Hypercar => "hyper",
        CarKind.Lmp2 => "lmp2",
        CarKind.Lmp3 => "lmp3",
        CarKind.Gte => "gte",
        CarKind.Gt3 => "gt3",
        _ => "",
    };

    /// <summary>Largeur et hauteur réelles (m) de la voiture vue de face.</summary>
    static (double Width, double Height) CarSize(CarKind kind) => kind switch
    {
        CarKind.Hypercar => (2.00, 1.07),
        CarKind.Lmp2 => (1.90, 1.04),
        CarKind.Lmp3 => (1.84, 1.02),
        CarKind.Gte => (2.04, 1.20),
        CarKind.Gt3 => (2.04, 1.25),
        _ => (1.95, 1.10),
    };

    void DrawCar(Graphics g, int w, float horizon, double focal, in Car car, bool withLabel)
    {
        var kind = Classify(car.Class);
        var (carWidth, carHeight) = CarSize(kind);

        double z = Math.Max(1.0, car.Lz - 2.0); // face avant de la voiture suiveuse
        double scale = focal / z;
        float cx = (float)(w / 2.0 - car.Lx * scale);
        float width = (float)(carWidth * scale);
        float bottom = (float)(horizon - (car.Ly - 0.8) * scale);
        float top = (float)(horizon - (car.Ly - 0.8 + carHeight) * scale);
        float height = bottom - top;
        if (width < 2 || height < 2)
            return;

        // Coordonnées normalisées : u de -0,5 (gauche) à 0,5 (droite), v de 0 (sol) à 1 (toit).
        PointF P(double u, double v) => new(cx + (float)(u * width), bottom - (float)(v * height));
        PointF[] Poly(params double[] uv)
        {
            var pts = new PointF[uv.Length / 2];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = P(uv[i * 2], uv[i * 2 + 1]);
            return pts;
        }
        RectangleF R(double u1, double v1, double u2, double v2)
        {
            var a = P(u1, v2);
            var b = P(u2, v1);
            return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
        }

        var color = ClassColor(car.Class);
        var box = new RectangleF(cx - width / 2, top, width, height);
        float gap = (float)(car.Lz - 4.6);
        using var outline = new Pen(gap < 10 ? Color.FromArgb(255, 60, 40) : Color.FromArgb(170, 0, 0, 0),
            gap < 10 ? Math.Max(2f, width * 0.025f) : 1f) { LineJoin = LineJoin.Round };
        using var bodyBrush = new LinearGradientBrush(box, Light(color, 0.25f), Dark(color, 0.5f), LinearGradientMode.Vertical);
        using var glass = new SolidBrush(Color.FromArgb(230, 14, 17, 24));
        using var black = new SolidBrush(Color.FromArgb(240, 12, 12, 14));
        using var light = new SolidBrush(car.Headlights ? Color.FromArgb(255, 255, 250, 215) : Color.FromArgb(220, 185, 185, 175));
        using var glow = new SolidBrush(Color.FromArgb(55, 255, 250, 200));

        // LMU : face avant propre au modèle (phares, calandre…), si la voiture est reconnue.
        var front = string.IsNullOrEmpty(car.Model) ? null : CarFronts.Find(KindName(kind), car.Model, car.Class);
        if (front != null)
        {
            using var dark = new SolidBrush(Dark(color, 0.45f));
            using var trim = new SolidBrush(Color.FromArgb(235, 150, 150, 155));
            CarFronts.Draw(g, front, P, width, new CarFronts.Paints(bodyBrush, dark, glass, black, trim, light, glow, car.Headlights, outline));
        }
        else if (kind is CarKind.Hypercar or CarKind.Lmp2 or CarKind.Lmp3) = kind is CarKind.Hypercar or CarKind.Lmp2 or CarKind.Lmp3;
        {
            // Proto : ailes avant bombées, nez bas, bulle de cockpit étroite au centre.
            double hump = kind == CarKind.Hypercar ? 0.62 : kind == CarKind.Lmp2 ? 0.56 : 0.52;
            double cab = kind == CarKind.Lmp3 ? 0.13 : 0.12;
            var body = Poly(
                -0.50, 0.04, -0.50, 0.34, -0.47, hump - 0.06, -0.38, hump, -0.26, hump - 0.02,
                -0.19, 0.40, -cab - 0.03, 0.44, -cab + 0.02, 0.93, cab - 0.02, 0.93, cab + 0.03, 0.44,
                0.19, 0.40, 0.26, hump - 0.02, 0.38, hump, 0.47, hump - 0.06, 0.50, 0.34, 0.50, 0.04);
            g.FillPolygon(bodyBrush, body);
            g.FillPolygon(glass, Poly(-cab + 0.005, 0.56, -cab + 0.035, 0.87, cab - 0.035, 0.87, cab - 0.005, 0.56));
            g.FillRectangle(black, R(-0.50, 0.0, 0.50, 0.07));   // lame avant
            g.FillRectangle(black, R(-0.13, 0.12, 0.13, 0.26));  // entrée d'air du nez
            g.DrawPolygon(outline, body);

            switch (kind)
            {
                case CarKind.Hypercar:
                    // Signature lumineuse : fines barres LED inclinées + lame centrale.
                    foreach (var side in new[] { -1.0, 1.0 })
                    {
                        var bar = Poly(side * 0.46, hump - 0.14, side * 0.30, hump - 0.10,
                                       side * 0.30, hump - 0.15, side * 0.46, hump - 0.19);
                        if (car.Headlights) g.FillEllipse(glow, R(side * 0.5 - 0.1, hump - 0.3, side * 0.5 + 0.1, hump));
                        g.FillPolygon(light, bar);
                    }
                    g.FillRectangle(light, R(-0.10, 0.30, 0.10, 0.33));
                    break;
                case CarKind.Lmp2:
                    // Deux petits phares rectangulaires dans chaque aile.
                    foreach (var side in new[] { -1.0, 1.0 })
                    {
                        if (car.Headlights) g.FillEllipse(glow, R(side * 0.37 - 0.12, 0.28, side * 0.37 + 0.12, 0.58));
                        g.FillRectangle(light, R(side * 0.44 - 0.05, 0.40, side * 0.44 + 0.02, 0.47));
                        g.FillRectangle(light, R(side * 0.33 - 0.03, 0.40, side * 0.33 + 0.04, 0.47));
                    }
                    break;
                default:
                    // LMP3 : un phare rond par aile.
                    foreach (var side in new[] { -1.0, 1.0 })
                    {
                        if (car.Headlights) g.FillEllipse(glow, R(side * 0.37 - 0.12, 0.26, side * 0.37 + 0.12, 0.56));
                        g.FillEllipse(light, R(side * 0.37 - 0.05, 0.36, side * 0.37 + 0.05, 0.47));
                    }
                    break;
            }
        }
        else
        {
            // GT : silhouette de voiture de route, large pare-brise, calandre, rétroviseurs.
            double roof = kind == CarKind.Gte ? 0.95 : 0.97;
            var body = Poly(
                -0.50, 0.04, -0.50, 0.44, -0.45, 0.55, -0.39, 0.58, -0.27, roof, 0.27, roof,
                0.39, 0.58, 0.45, 0.55, 0.50, 0.44, 0.50, 0.04);
            g.FillPolygon(bodyBrush, body);
            g.FillPolygon(glass, Poly(-0.35, 0.61, -0.24, roof - 0.06, 0.24, roof - 0.06, 0.35, 0.61));
            g.FillRectangle(black, R(-0.50, 0.0, 0.50, 0.07));   // lame avant
            if (kind == CarKind.Gte)
                g.FillRectangle(black, R(-0.16, 0.14, 0.16, 0.28)); // calandre plus petite
            else
                g.FillPolygon(black, Poly(-0.24, 0.12, 0.24, 0.12, 0.20, 0.33, -0.20, 0.33)); // grande calandre GT3
            g.FillRectangle(bodyBrush, R(-0.57, 0.59, -0.42, 0.66));  // rétroviseurs
            g.FillRectangle(bodyBrush, R(0.42, 0.59, 0.57, 0.66));
            g.DrawPolygon(outline, body);

            foreach (var side in new[] { -1.0, 1.0 })
            {
                if (car.Headlights) g.FillEllipse(glow, R(side * 0.37 - 0.14, 0.26, side * 0.37 + 0.14, 0.56));
                var lamp = Poly(side * 0.46, 0.46, side * 0.28, 0.44, side * 0.29, 0.38, side * 0.46, 0.38);
                g.FillPolygon(light, lamp);
            }
        }

        if (!withLabel)
            return;

        // Étiquette
        float fontSize = (float)Math.Clamp(height * 0.35, 11, 26);
        var font = GetFont(fontSize);
        string distance = $"{Math.Max(0, car.Lz - 4.6):0} m";
        if (Math.Abs(car.Closing) >= 3)
            distance += car.Closing > 0 ? $"  ▲{car.Closing:0}" : $"  ▼{-car.Closing:0}";

        var labelSize = g.MeasureString(car.Label, font);
        float labelY = top - labelSize.Height * 2.0f;
        using (var bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            g.FillRectangle(bg, cx - labelSize.Width / 2 - 3, labelY, labelSize.Width + 6, labelSize.Height * 2);
        g.DrawString(car.Label, font, Brushes.White, cx - labelSize.Width / 2, labelY);
        var distSize = g.MeasureString(distance, font);
        var distBrush = car.Closing > 15 ? Brushes.OrangeRed : Brushes.Gainsboro;
        g.DrawString(distance, font, distBrush, cx - distSize.Width / 2, labelY + labelSize.Height);
    }

    static void DrawSideWarning(Graphics g, int w, int h, bool left)
    {
        float bw = w * 0.07f;
        var rect = left ? new RectangleF(0, 0, bw, h) : new RectangleF(w - bw, 0, bw, h);
        using var brush = new LinearGradientBrush(rect,
            left ? Color.FromArgb(230, 255, 90, 0) : Color.FromArgb(0, 255, 90, 0),
            left ? Color.FromArgb(0, 255, 90, 0) : Color.FromArgb(230, 255, 90, 0),
            LinearGradientMode.Horizontal);
        g.FillRectangle(brush, rect);
    }

    void DrawCenteredText(Graphics g, int w, int h, string text)
    {
        var font = GetFont(Math.Clamp(h / 18f, 11, 24));
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, Brushes.Silver, (w - size.Width) / 2, (h - size.Height) / 2);
    }

    Font GetFont(float size)
    {
        int key = (int)Math.Round(size);
        if (!_fonts.TryGetValue(key, out var font))
            _fonts[key] = font = new Font("Segoe UI", key, FontStyle.Bold, GraphicsUnit.Pixel);
        return font;
    }

    static Color ClassColor(string cls) => Classify(cls) switch
    {
        CarKind.Hypercar => Color.FromArgb(215, 35, 45),
        CarKind.Lmp2 => Color.FromArgb(35, 105, 225),
        CarKind.Lmp3 => Color.FromArgb(145, 70, 200),
        CarKind.Gte => Color.FromArgb(235, 130, 20),
        CarKind.Gt3 => Color.FromArgb(30, 165, 80),
        _ => Color.FromArgb(160, 160, 160),
    };

    static Color Light(Color c, float f) =>
        Color.FromArgb(c.A, c.R + (int)((255 - c.R) * f), c.G + (int)((255 - c.G) * f), c.B + (int)((255 - c.B) * f));

    static Color Dark(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(1000);
        _track.Save();
        foreach (var source in _sources)
            source.Dispose();
        foreach (var f in _fonts.Values)
            f.Dispose();
    }
}
