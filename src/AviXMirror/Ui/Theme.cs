using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AviXMirror.Ui;

/// <summary>
/// Charte graphique de l'application (thème sombre, couleur d'accent et police réglables dans
/// « 7. Apparence »). Tous les contrôles lisent ces valeurs au moment de se dessiner.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 15, 18);
    public static readonly Color Surface = Color.FromArgb(23, 25, 30);
    public static readonly Color SurfaceRaised = Color.FromArgb(31, 34, 41);
    public static readonly Color Border = Color.FromArgb(44, 48, 57);
    public static readonly Color Text = Color.FromArgb(236, 238, 241);
    public static readonly Color TextMuted = Color.FromArgb(140, 146, 156);
    public static readonly Color Success = Color.FromArgb(53, 196, 106);
    public static readonly Color Danger = Color.FromArgb(229, 72, 77);

    public const string DefaultAccent = "#C4C007"; // jaune-vert du logo AVIX
    public const string DefaultFont = "Bahnschrift";

    public static Color Accent { get; private set; } = ColorTranslator.FromHtml(DefaultAccent);
    public static string FontName { get; private set; } = DefaultFont;

    /// <summary>Accent assombri (survol, appui).</summary>
    public static Color AccentDark => Blend(Accent, Color.Black, 0.25f);

    public static void Configure(string? accentHex, string? fontName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(accentHex))
                Accent = ColorTranslator.FromHtml(accentHex.Trim());
        }
        catch
        {
            Accent = ColorTranslator.FromHtml(DefaultAccent);
        }

        FontName = IsInstalled(fontName) ? fontName!.Trim()
                 : IsInstalled(DefaultFont) ? DefaultFont
                 : "Segoe UI";
    }

    static bool IsInstalled(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new(FontName, size, style, GraphicsUnit.Point);

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barre de titre sombre (Windows 10 20H1+) et teintée (Windows 11).</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
            int caption = ColorTranslator.ToWin32(Background);
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch
        {
            // Ancien Windows : barre de titre standard.
        }
    }

    static Image? _logo;

    /// <summary>Logo AVIX (« SIMRACING & IMPRESSION 3D »), fond transparent, texte clair pour fond sombre.</summary>
    public static Image Logo
    {
        get
        {
            if (_logo == null)
            {
                using var stream = typeof(Theme).Assembly.GetManifestResourceStream("Brand.avix-logo.png")
                    ?? throw new InvalidOperationException("Logo manquant");
                _logo = new Bitmap(stream);
            }
            return _logo;
        }
    }

    /// <summary>Dessine le logo centré dans <paramref name="box"/> en gardant ses proportions.</summary>
    public static RectangleF DrawLogo(Graphics g, RectangleF box)
    {
        var logo = Logo;
        float scale = Math.Min(box.Width / logo.Width, box.Height / logo.Height);
        var r = new RectangleF(box.X + (box.Width - logo.Width * scale) / 2, box.Y + (box.Height - logo.Height * scale) / 2,
            logo.Width * scale, logo.Height * scale);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        lock (logo)
            g.DrawImage(logo, r);
        return r;
    }

    /// <summary>Icône de l'application : rétroviseur stylisé sur fond d'accent.</summary>
    public static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            Smooth(g);
            g.Clear(Color.Transparent);
            using var bg = RoundedRect(new RectangleF(2, 2, 60, 60), 14);
            using var accent = new SolidBrush(Accent);
            g.FillPath(accent, bg);
            using var mirror = RoundedRect(new RectangleF(10, 22, 44, 20), 8);
            using var dark = new SolidBrush(Background);
            g.FillPath(dark, mirror);
            using var glint = new Pen(Color.FromArgb(200, 255, 255, 255), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(glint, 18, 36, 28, 28);
            g.DrawLine(glint, 30, 36, 36, 31);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
