using System.Drawing.Drawing2D;
using System.Globalization;

namespace AviXMirror.Radar;

/// <summary>
/// Faces avant propres à chaque voiture de Le Mans Ultimate (phares, calandre, entrées d'air), décrites
/// dans « CarFronts.txt » (ressource intégrée) et reconnues par des mots-clés du nom de la voiture.
/// </summary>
public static class CarFronts
{
    public sealed class Front
    {
        public string Id = "";
        public string[] Kinds = Array.Empty<string>();
        public string[] Keywords = Array.Empty<string>();
        public readonly List<Op> Ops = new();
    }

    public sealed record Op(string Paint, string Shape, bool Mirror, float[] N);

    /// <summary>Pinceaux et crayons d'une voiture (couleur de sa classe, phares allumés ou non).</summary>
    public sealed record Paints(Brush Body, Brush Dark, Brush Glass, Brush Black, Brush Trim, Brush Light, Brush Glow,
        bool Lit, Pen Outline);

    static readonly Lazy<List<Front>> All = new(Load);
    static readonly Dictionary<string, Front?> Cache = new();

    /// <summary>Face avant correspondant à la voiture (catégorie « hyper », « gt3 »…), ou null.</summary>
    public static Front? Find(string kind, string vehicleName, string vehicleClass)
    {
        string text = " " + (vehicleName + " " + vehicleClass).ToUpperInvariant().Replace('_', ' ') + " ";
        string key = kind + "|" + text;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            var found = All.Value.FirstOrDefault(f => f.Kinds.Contains(kind) && f.Keywords.Any(text.Contains));
            if (Cache.Count > 500)
                Cache.Clear();
            Cache[key] = found;
            return found;
        }
    }

    /// <param name="point">Coordonnées normalisées (u de -0,5 à 0,5, v de 0 au sol à 1) -> image.</param>
    /// <param name="width">Largeur de la voiture à l'écran (px), pour l'épaisseur des traits.</param>
    public static void Draw(Graphics g, Front front, Func<double, double, PointF> point, float width, Paints paints)
    {
        foreach (var op in front.Ops)
        {
            if (op.Paint == "glow" && !paints.Lit)
                continue;
            var brush = op.Paint switch
            {
                "body" => paints.Body,
                "dark" => paints.Dark,
                "glass" => paints.Glass,
                "trim" => paints.Trim,
                "light" => paints.Light,
                "glow" => paints.Glow,
                _ => paints.Black,
            };
            DrawShape(g, op, brush, point, width, 1, paints);
            if (op.Mirror)
                DrawShape(g, op, brush, point, width, -1, paints);
        }
    }

    static void DrawShape(Graphics g, Op op, Brush brush, Func<double, double, PointF> point, float width, int side, Paints paints)
    {
        var n = op.N;
        switch (op.Shape)
        {
            case "poly":
            {
                var pts = new PointF[n.Length / 2];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = point(side * n[i * 2], n[i * 2 + 1]);
                g.FillPolygon(brush, pts);
                if (op.Paint == "body" && !op.Mirror)
                    g.DrawPolygon(paints.Outline, pts);
                break;
            }
            case "rect":
            case "ell":
            {
                var a = point(side * n[0], n[1]);
                var b = point(side * n[2], n[3]);
                var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                if (op.Shape == "rect")
                    g.FillRectangle(brush, r);
                else
                    g.FillEllipse(brush, r);
                break;
            }
            case "line":
            {
                var pts = new PointF[(n.Length - 1) / 2];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = point(side * n[1 + i * 2], n[2 + i * 2]);
                using var pen = new Pen(brush, Math.Max(1f, n[0] * width)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                if (pts.Length > 1)
                    g.DrawLines(pen, pts);
                break;
            }
        }
    }

    static List<Front> Load()
    {
        var list = new List<Front>();
        try
        {
            using var stream = typeof(CarFronts).Assembly.GetManifestResourceStream("Radar.CarFronts.txt");
            if (stream == null)
                return list;
            using var reader = new StreamReader(stream);
            list = Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Faces avant : " + ex.Message);
        }
        return list;
    }

    public static List<Front> Parse(string text)
    {
        var list = new List<Front>();
        Front? current = null;
        foreach (var raw in text.Split('\n'))
        {
            int hash = raw.IndexOf('#');
            var line = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length == 0)
                continue;
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t[0] == "car")
            {
                current = new Front
                {
                    Id = t[1],
                    Kinds = t[2].Split(','),
                    Keywords = t.Skip(3).Select(k => " " + k.ToUpperInvariant().Replace('_', ' ')).ToArray(),
                };
                list.Add(current);
                continue;
            }
            if (current == null)
                continue;
            switch (t[0])
            {
                case "proto":
                {
                    double h = Num(t[1]), c = Num(t[2]);
                    current.Ops.Add(Make("body", "poly", false, -0.50, 0.04, -0.50, 0.34, -0.47, h - 0.06, -0.38, h, -0.26, h - 0.02,
                        -0.19, 0.40, -c - 0.03, 0.44, -c + 0.02, 0.93, c - 0.02, 0.93, c + 0.03, 0.44,
                        0.19, 0.40, 0.26, h - 0.02, 0.38, h, 0.47, h - 0.06, 0.50, 0.34, 0.50, 0.04));
                    current.Ops.Add(Make("glass", "poly", false, -c + 0.005, 0.56, -c + 0.035, 0.87, c - 0.035, 0.87, c - 0.005, 0.56));
                    current.Ops.Add(Make("black", "rect", false, -0.50, 0.0, 0.50, 0.07));
                    break;
                }
                case "gt":
                {
                    double r = Num(t[1]);
                    current.Ops.Add(Make("body", "poly", false, -0.50, 0.04, -0.50, 0.44, -0.45, 0.55, -0.39, 0.58, -0.27, r,
                        0.27, r, 0.39, 0.58, 0.45, 0.55, 0.50, 0.44, 0.50, 0.04));
                    current.Ops.Add(Make("glass", "poly", false, -0.35, 0.61, -0.24, r - 0.06, 0.24, r - 0.06, 0.35, 0.61));
                    current.Ops.Add(Make("black", "rect", false, -0.50, 0.0, 0.50, 0.07));
                    current.Ops.Add(Make("body", "rect", true, 0.42, 0.59, 0.57, 0.66)); // rétroviseurs
                    break;
                }
                default:
                {
                    bool mirror = t.Length > 2 && t[2] == "m";
                    var numbers = t.Skip(mirror ? 3 : 2).Select(x => (float)Num(x)).ToArray();
                    current.Ops.Add(new Op(t[0], t[1], mirror, numbers));
                    break;
                }
            }
        }
        return list;
    }

    static Op Make(string paint, string shape, bool mirror, params double[] n) =>
        new(paint, shape, mirror, n.Select(x => (float)x).ToArray());

    static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
