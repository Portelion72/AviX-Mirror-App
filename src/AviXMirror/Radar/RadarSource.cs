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
    public Size FallbackSize { get; set; } = new(1280, 400);

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

    void DrawCar(Graphics g, int w, float horizon, double focal, in Car car)
    {
        double z = Math.Max(1.0, car.Lz - 2.0); // face avant de la voiture suiveuse
        double scale = focal / z;
        float cx = (float)(w / 2.0 - car.Lx * scale);
        float width = (float)(1.95 * scale);
        float bottom = (float)(horizon - (car.Ly - 0.8) * scale);
        float top = (float)(horizon - (car.Ly - 0.8 + 1.05) * scale);
        float height = bottom - top;
        if (width < 2 || height < 2)
            return;

        var body = new RectangleF(cx - width / 2, top, width, height);
        var color = ClassColor(car.Class);

        using (var path = RoundedRect(body, Math.Max(2f, width * 0.12f)))
        using (var brush = new LinearGradientBrush(body, color, Dark(color, 0.45f), LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
            float gap = (float)(car.Lz - 4.6);
            var outline = gap < 10 ? Color.FromArgb(255, 60, 40) : Color.FromArgb(160, 0, 0, 0);
            using var pen = new Pen(outline, gap < 10 ? Math.Max(2f, width * 0.03f) : 1f);
            g.DrawPath(pen, path);
        }

        // Pare-brise
        var glass = new[]
        {
            new PointF(cx - width * 0.30f, top + height * 0.08f),
            new PointF(cx + width * 0.30f, top + height * 0.08f),
            new PointF(cx + width * 0.40f, top + height * 0.45f),
            new PointF(cx - width * 0.40f, top + height * 0.45f),
        };
        using (var glassBrush = new SolidBrush(Color.FromArgb(200, 15, 18, 24)))
            g.FillPolygon(glassBrush, glass);

        // Phares
        var lightColor = car.Headlights ? Color.FromArgb(255, 255, 250, 210) : Color.FromArgb(200, 170, 170, 160);
        float lw = width * 0.2f, lh = height * 0.16f, ly = top + height * 0.58f;
        using (var light = new SolidBrush(lightColor))
        {
            g.FillEllipse(light, cx - width * 0.46f, ly, lw, lh);
            g.FillEllipse(light, cx + width * 0.26f, ly, lw, lh);
        }
        if (car.Headlights)
        {
            using var glow = new SolidBrush(Color.FromArgb(60, 255, 250, 200));
            g.FillEllipse(glow, cx - width * 0.55f, ly - lh, lw * 1.8f, lh * 3);
            g.FillEllipse(glow, cx + width * 0.18f, ly - lh, lw * 1.8f, lh * 3);
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

    static Color ClassColor(string cls)
    {
        var c = cls.ToUpperInvariant();
        if (c.Contains("HYPER") || c.Contains("LMH") || c.Contains("LMDH")) return Color.FromArgb(215, 35, 45);
        if (c.Contains("LMP2")) return Color.FromArgb(35, 105, 225);
        if (c.Contains("LMP3")) return Color.FromArgb(145, 70, 200);
        if (c.Contains("GT")) return Color.FromArgb(30, 165, 80);
        return Color.FromArgb(200, 140, 40);
    }

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
