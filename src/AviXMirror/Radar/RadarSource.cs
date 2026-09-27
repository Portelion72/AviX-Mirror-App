using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AviXMirror.Util;

namespace AviXMirror.Radar;

/// <summary>
/// Rétroviseur synthétique : dessine en perspective les voitures situées derrière le joueur
/// à partir des positions fournies par la mémoire partagée rF2 (LMU).
/// Aucun rendu du jeu n'est nécessaire, donc rien ne s'affiche sur l'écran principal.
/// </summary>
public sealed class RadarSource : IDisposable
{
    const double ScoringPollSeconds = 0.05;
    const double MaxExtrapolationSeconds = 0.4;

    readonly FrameBuffer _output;
    readonly RF2ScoringReader _reader = new();
    readonly Thread _thread;
    volatile bool _running = true;
    volatile Settings _settings;

    RF2Scoring _scoring;
    bool _hasData;
    double _lastET = double.NaN;
    readonly Stopwatch _sinceUpdate = Stopwatch.StartNew();

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
        double nextPoll = 0;
        while (_running)
        {
            var s = _settings;
            double frameTime = 1.0 / Math.Clamp(s.TargetFps, 5, 240);
            double start = clock.Elapsed.TotalSeconds;

            if (start >= nextPoll)
            {
                Poll();
                nextPoll = start + ScoringPollSeconds;
            }

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
        if (!_reader.TryRead(out var scoring))
        {
            _hasData = false;
            Status = "En attente de LMU (plugin rF2 Shared Memory Map)…";
            return;
        }

        int n = scoring.ScoringInfo.NumVehicles;
        if (n < 0 || n > RF2Scoring.MaxVehicles || scoring.Vehicles == null)
        {
            _hasData = false;
            Status = "Données de télémétrie invalides.";
            return;
        }

        if (scoring.ScoringInfo.CurrentET != _lastET)
        {
            _lastET = scoring.ScoringInfo.CurrentET;
            _sinceUpdate.Restart();
        }
        _scoring = scoring;
        _hasData = true;
        Status = n == 0 ? "LMU connecté — pas de session en cours." : $"LMU connecté — {n} voitures.";
    }

    struct Car
    {
        public double Lx, Ly, Lz, Closing;
        public string Label, Class;
        public bool Headlights;
    }

    static RF2Vec3 WorldVelocity(in RF2VehicleScoring v) => new()
    {
        X = Dot(v.Ori[0], v.LocalVel),
        Y = Dot(v.Ori[1], v.LocalVel),
        Z = Dot(v.Ori[2], v.LocalVel),
    };

    static double Dot(in RF2Vec3 a, in RF2Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    void Render(Graphics g, int w, int h, Settings s)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float horizon = h * 0.42f;
        DrawBackground(g, w, h, horizon);

        if (!_hasData || _scoring.ScoringInfo.NumVehicles == 0)
        {
            DrawCenteredText(g, w, h, Status);
            return;
        }

        var vehicles = _scoring.Vehicles;
        int count = _scoring.ScoringInfo.NumVehicles;
        int playerIndex = -1;
        for (int i = 0; i < count; i++)
            if (vehicles[i].IsPlayer != 0) { playerIndex = i; break; }

        if (playerIndex < 0)
        {
            DrawCenteredText(g, w, h, "Pas de voiture joueur (spectateur ?)");
            return;
        }

        double dt = Math.Min(_sinceUpdate.Elapsed.TotalSeconds, MaxExtrapolationSeconds);
        ref readonly var player = ref vehicles[playerIndex];
        if (player.Ori == null)
            return;
        var pv = WorldVelocity(player);
        var pp = new RF2Vec3 { X = player.Pos.X + pv.X * dt, Y = player.Pos.Y + pv.Y * dt, Z = player.Pos.Z + pv.Z * dt };

        var behind = new List<Car>();
        bool warnLeft = false, warnRight = false;

        for (int i = 0; i < count; i++)
        {
            if (i == playerIndex)
                continue;
            ref readonly var v = ref vehicles[i];
            if (v.Ori == null || v.InGarageStall != 0)
                continue;

            var vv = WorldVelocity(v);
            var d = new RF2Vec3
            {
                X = v.Pos.X + vv.X * dt - pp.X,
                Y = v.Pos.Y + vv.Y * dt - pp.Y,
                Z = v.Pos.Z + vv.Z * dt - pp.Z,
            };
            // Monde -> repère local du joueur (transposée de la matrice d'orientation).
            // Repère rF2 : x = gauche, y = haut, z = arrière.
            double lx = player.Ori[0].X * d.X + player.Ori[1].X * d.Y + player.Ori[2].X * d.Z;
            double ly = player.Ori[0].Y * d.X + player.Ori[1].Y * d.Y + player.Ori[2].Y * d.Z;
            double lz = player.Ori[0].Z * d.X + player.Ori[1].Z * d.Y + player.Ori[2].Z * d.Z;
            if (s.RadarInvertLateral)
                lx = -lx;

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
            double relZ = player.Ori[0].Z * rel.X + player.Ori[1].Z * rel.Y + player.Ori[2].Z * rel.Z;

            string name = RF2ScoringReader.DecodeString(v.DriverName);
            behind.Add(new Car
            {
                Lx = lx,
                Ly = ly,
                Lz = lz,
                Closing = -relZ * 3.6,
                Label = s.RadarShowNames ? $"P{v.Place} {ShortName(name)}" : $"P{v.Place}",
                Class = RF2ScoringReader.DecodeString(v.VehicleClass),
                Headlights = v.Headlights != 0,
            });
        }

        double fov = Math.Clamp(s.RadarFov, 20, 150) * Math.PI / 180;
        double focal = w / 2.0 / Math.Tan(fov / 2);

        foreach (var car in behind.OrderByDescending(c => c.Lz))
            DrawCar(g, w, horizon, focal, car);

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

        using (var ground = new LinearGradientBrush(new RectangleF(0, horizon, w, h - horizon),
                   Color.FromArgb(38, 40, 42), Color.FromArgb(18, 18, 20), LinearGradientMode.Vertical))
            g.FillRectangle(ground, 0, horizon, w, h - horizon);

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

    enum CarKind { Hypercar, Lmp2, Lmp3, Gte, Gt3, Other }

    static CarKind Classify(string cls)
    {
        var c = cls.ToUpperInvariant();
        if (c.Contains("HYPER") || c.Contains("LMH") || c.Contains("LMDH")) return CarKind.Hypercar;
        if (c.Contains("LMP2")) return CarKind.Lmp2;
        if (c.Contains("LMP3")) return CarKind.Lmp3;
        if (c.Contains("GTE")) return CarKind.Gte;
        if (c.Contains("GT")) return CarKind.Gt3;
        return CarKind.Other;
    }

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

    void DrawCar(Graphics g, int w, float horizon, double focal, in Car car)
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

        bool prototype = kind is CarKind.Hypercar or CarKind.Lmp2 or CarKind.Lmp3;
        if (prototype)
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
        _reader.Dispose();
        foreach (var f in _fonts.Values)
            f.Dispose();
    }
}
