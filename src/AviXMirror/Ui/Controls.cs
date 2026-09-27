using System.ComponentModel;
using System.Drawing.Drawing2D;
using AviXMirror.Output;
using AviXMirror.Util;

namespace AviXMirror.Ui;

/// <summary>Base des contrôles dessinés à la main (double tampon, fond transparent au parent).</summary>
public abstract class PaintedControl : Control
{
    protected bool Hover { get; private set; }
    protected bool Pressed { get; private set; }

    protected PaintedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
}

/// <summary>Bouton plat arrondi : principal (accent) ou secondaire.</summary>
public sealed class FlatButton : PaintedControl
{
    [DefaultValue(false)]
    public bool Primary { get; set; }

    /// <summary>Couleur de remplissage forcée (ex. rouge pour « Arrêter »).</summary>
    public Color? FillOverride { get; set; }

    public FlatButton()
    {
        Cursor = Cursors.Hand;
        Height = 40;
        TabStop = true;
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 8);

        Color fill, border, text;
        if (Primary || FillOverride.HasValue)
        {
            var baseColor = FillOverride ?? Theme.Accent;
            fill = Pressed ? Theme.Blend(baseColor, Color.Black, 0.3f) : Hover ? Theme.Blend(baseColor, Color.White, 0.12f) : baseColor;
            border = fill;
            text = Color.White;
        }
        else
        {
            fill = Pressed ? Theme.Border : Hover ? Theme.Blend(Theme.SurfaceRaised, Color.White, 0.05f) : Theme.SurfaceRaised;
            border = Hover ? Theme.Accent : Theme.Border;
            text = Enabled ? Theme.Text : Theme.TextMuted;
        }

        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var pen = new Pen(border))
            g.DrawPath(pen, path);

        using var font = Theme.Font(Primary ? 12f : 9.5f, Primary ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, Primary ? Text.ToUpperInvariant() : Text, font, Rectangle.Round(rect), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Tuile de choix du mode (titre, description, trait d'accent si sélectionnée).</summary>
public sealed class ModeTile : PaintedControl
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public ModeTile()
    {
        Cursor = Cursors.Hand;
        Height = 64;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 8);
        var fill = Selected ? Theme.Blend(Theme.SurfaceRaised, Theme.Accent, 0.10f)
                 : Hover ? Theme.Blend(Theme.SurfaceRaised, Color.White, 0.04f) : Theme.SurfaceRaised;
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var pen = new Pen(Selected ? Theme.Accent : Theme.Border, Selected ? 1.5f : 1f))
            g.DrawPath(pen, path);

        if (Selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, 0, 12, 4, Height - 24);
        }

        using var titleFont = Theme.Font(11f, FontStyle.Bold);
        using var descFont = Theme.Font(8.5f);
        TextRenderer.DrawText(g, Title.ToUpperInvariant(), titleFont, new Rectangle(16, 10, Width - 24, 22),
            Selected ? Theme.Text : Theme.Blend(Theme.Text, Theme.TextMuted, 0.3f), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Description, descFont, new Rectangle(16, 32, Width - 24, Height - 36),
            Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Petit titre de section en capitales, couleur atténuée.</summary>
public sealed class SectionLabel : PaintedControl
{
    public SectionLabel(string text)
    {
        Text = text;
        Height = 26;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var font = Theme.Font(8.5f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, Text.ToUpperInvariant(), font, new Rectangle(0, 6, Width, Height - 6),
            Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.Bottom);
    }
}

/// <summary>Carte : fond de surface arrondi avec marge intérieure.</summary>
public sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        Theme.Smooth(g);
        using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12);
        using var brush = new SolidBrush(Theme.Surface);
        g.FillPath(brush, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }
}

/// <summary>En-tête : logotype AVIX_3D, nom du produit et pastille d'état.</summary>
public sealed class HeaderBar : Control
{
    string _state = "ARRÊTÉ";
    bool _running;

    public HeaderBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 72;
        Dock = DockStyle.Top;
    }

    public void SetState(bool running, string text)
    {
        if (running == _running && text == _state)
            return;
        _running = running;
        _state = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.Clear(Theme.Background);

        // Logo AVIX.
        var logo = Theme.DrawLogo(g, new RectangleF(20, 10, 190, Height - 22));
        int logoWidth = (int)(logo.Right - 24);

        using var product = Theme.Font(11f, FontStyle.Regular);
        using var sep = new Pen(Theme.Border, 1);
        int x = 24 + logoWidth + 16;
        g.DrawLine(sep, x, 22, x, 48);
        TextRenderer.DrawText(g, "MIRROR", product, new Point(x + 14, 19), Theme.Text, TextFormatFlags.NoPadding);
        using var sub = Theme.Font(8f);
        TextRenderer.DrawText(g, "Rétroviseur VoCore", sub, new Point(x + 14, 38), Theme.TextMuted, TextFormatFlags.NoPadding);

        // Pastille d'état à droite.
        using var pillFont = Theme.Font(8.5f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(g, _state, pillFont);
        var pill = new RectangleF(Width - 24 - size.Width - 34, 22, size.Width + 34, 28);
        using var pillPath = Theme.RoundedRect(pill, 14);
        var dot = _running ? Theme.Success : Theme.TextMuted;
        using (var bg = new SolidBrush(Theme.Blend(Theme.Surface, dot, 0.12f)))
            g.FillPath(bg, pillPath);
        using (var border = new Pen(Theme.Blend(Theme.Border, dot, 0.4f)))
            g.DrawPath(border, pillPath);
        using (var dotBrush = new SolidBrush(dot))
            g.FillEllipse(dotBrush, pill.X + 12, pill.Y + 10, 8, 8);
        TextRenderer.DrawText(g, _state, pillFont, new Point((int)pill.X + 26, (int)pill.Y + 6), Theme.Text, TextFormatFlags.NoPadding);

        // Liseré d'accent en bas.
        using var line = new LinearGradientBrush(new Rectangle(0, Height - 2, Width, 2), Theme.Accent, Theme.Background, LinearGradientMode.Horizontal);
        g.FillRectangle(line, 0, Height - 2, Width, 2);
    }
}

/// <summary>Aperçu en direct de l'image envoyée au VoCore, dans un cadre en forme de rétroviseur.</summary>
public sealed class MirrorPreview : Control
{
    readonly FrameBuffer _frames;
    readonly Func<(Color[] Left, Color[] Right)>? _leds;
    int _pending;

    public MirrorPreview(FrameBuffer frames, Func<(Color[] Left, Color[] Right)>? leds = null)
    {
        _frames = frames;
        _leds = leds;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _frames.Updated += OnFrame;
    }

    void OnFrame()
    {
        // Aperçu limité à ~15 images/s pour ne pas charger l'interface.
        if (!IsHandleCreated || Interlocked.Exchange(ref _pending, 1) == 1)
            return;
        Task.Delay(66).ContinueWith(_ =>
        {
            try { BeginInvoke(() => { Interlocked.Exchange(ref _pending, 0); Invalidate(); }); }
            catch { Interlocked.Exchange(ref _pending, 0); }
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        // Cadre au format du VoCore 7,8" (1280 x 400), avec une marge de chaque côté pour les LEDs.
        const float ledGutter = 26;
        float ratio = 1280f / 400f;
        float w = Width - 2 - ledGutter * 2, h = w / ratio;
        if (h > Height - 2) { h = Height - 2; w = h * ratio; }
        var frame = new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        using var path = Theme.RoundedRect(frame, h * 0.18f);

        using (var black = new SolidBrush(Color.Black))
            g.FillPath(black, path);

        bool drawn;
        var saved = g.Save();
        g.SetClip(path);
        g.TranslateTransform(frame.X, frame.Y);
        drawn = _frames.Read(bmp => FrameRenderer.Draw(g, bmp, Size.Round(frame.Size), 0, false, true));
        g.Restore(saved);

        if (!drawn)
        {
            using var font = Theme.Font(10f, FontStyle.Bold);
            TextRenderer.DrawText(g, "AUCUN SIGNAL", font, Rectangle.Round(frame), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        Theme.Smooth(g);
        using var pen = new Pen(Theme.Border, 2);
        g.DrawPath(pen, path);

        // LEDs du spotter : barrette gauche et barrette droite, de l'arrière (bas) vers l'avant (haut).
        if (_leds != null)
        {
            var (left, right) = _leds();
            DrawLedBar(g, left, frame.Left - ledGutter + 6, frame);
            DrawLedBar(g, right, frame.Right + 8, frame);
        }
    }

    static void DrawLedBar(Graphics g, Color[] leds, float x, RectangleF frame)
    {
        if (leds.Length == 0)
            return;
        float step = frame.Height / leds.Length;
        float d = Math.Min(12, step * 0.7f);
        for (int i = 0; i < leds.Length; i++)
        {
            var c = leds[i];
            bool lit = c.R + c.G + c.B > 0;
            // Aperçu à pleine intensité (la luminosité réglée n'est pas représentative à l'écran).
            int max = Math.Max(1, Math.Max((int)c.R, Math.Max((int)c.G, (int)c.B)));
            var shown = lit ? Color.FromArgb(c.R * 255 / max, c.G * 255 / max, c.B * 255 / max) : Theme.SurfaceRaised;
            float y = frame.Bottom - step * (i + 0.5f) - d / 2;
            using var brush = new SolidBrush(shown);
            g.FillEllipse(brush, x, y, d, d);
            if (lit)
            {
                using var glow = new SolidBrush(Color.FromArgb(60, shown));
                g.FillEllipse(glow, x - 4, y - 4, d + 8, d + 8);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _frames.Updated -= OnFrame;
        base.Dispose(disposing);
    }
}
