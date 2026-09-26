using System.Drawing.Drawing2D;
using AviXMirror.Capture;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>Affiche l'image capturée entière et permet de sélectionner la zone du rétro à la souris.</summary>
public sealed class CalibrationForm : Form
{
    readonly WgcCapture _capture;
    readonly FrameBuffer _frames;
    readonly PictureBox _picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 150 };
    Bitmap? _image;
    Point? _dragStart;
    Rectangle _selection; // en pixels de l'image source

    public Rectangle Selection => _selection;

    public CalibrationForm(WgcCapture capture, FrameBuffer frames, Rectangle current)
    {
        _capture = capture;
        _frames = frames;
        _selection = current;

        Text = "Calibrer la zone du rétroviseur — tracez un rectangle autour du rétro virtuel";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
        };
        var ok = new Button { Text = "Valider", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
        var full = new Button { Text = "Image entière", AutoSize = true };
        full.Click += (_, _) => { _selection = Rectangle.Empty; _picture.Invalidate(); };
        var hint = new Label
        {
            Text = "Astuce : dans LMU, affichez le rétro virtuel (touche du HUD) avant de calibrer.",
            AutoSize = true,
            Padding = new Padding(0, 6, 20, 0),
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel, full, hint });
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_picture);
        Controls.Add(buttons);

        _picture.Paint += OnPicturePaint;
        _picture.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
        _picture.MouseMove += OnMouseMove;
        _picture.MouseUp += (_, _) => _dragStart = null;

        _capture.FullFrame = true;
        _refresh.Tick += (_, _) => RefreshImage();
        _refresh.Start();
    }

    void RefreshImage()
    {
        var snapshot = _frames.Snapshot();
        if (snapshot == null)
            return;
        _image?.Dispose();
        _image = snapshot;
        _picture.Invalidate();
    }

    RectangleF ImageRect()
    {
        if (_image == null)
            return RectangleF.Empty;
        var c = _picture.ClientSize;
        float scale = Math.Min((float)c.Width / _image.Width, (float)c.Height / _image.Height);
        float w = _image.Width * scale, h = _image.Height * scale;
        return new RectangleF((c.Width - w) / 2, (c.Height - h) / 2, w, h);
    }

    Point ToImage(Point p)
    {
        var r = ImageRect();
        if (_image == null || r.Width <= 0)
            return Point.Empty;
        float scale = _image.Width / r.Width;
        int x = (int)Math.Round((p.X - r.X) * scale);
        int y = (int)Math.Round((p.Y - r.Y) * scale);
        return new Point(Math.Clamp(x, 0, _image.Width), Math.Clamp(y, 0, _image.Height));
    }

    void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStart == null || e.Button != MouseButtons.Left)
            return;
        var a = ToImage(_dragStart.Value);
        var b = ToImage(e.Location);
        _selection = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        _picture.Invalidate();
    }

    void OnPicturePaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_image == null)
        {
            TextRenderer.DrawText(g, "En attente d'une image de la capture…", Font, _picture.ClientRectangle,
                Color.Silver, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var r = ImageRect();
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.DrawImage(_image, r);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            float scale = r.Width / _image.Width;
            var sel = new RectangleF(r.X + _selection.X * scale, r.Y + _selection.Y * scale,
                _selection.Width * scale, _selection.Height * scale);
            using var shade = new Region(r);
            shade.Exclude(sel);
            using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                g.FillRegion(dim, shade);
            using var pen = new Pen(Color.Lime, 2);
            g.DrawRectangle(pen, sel.X, sel.Y, sel.Width, sel.Height);
            var text = $"{_selection.Width}x{_selection.Height} @ {_selection.X},{_selection.Y}";
            TextRenderer.DrawText(g, text, Font, new Point((int)sel.X + 4, (int)sel.Y + 4), Color.Lime);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refresh.Stop();
        _refresh.Dispose();
        _capture.FullFrame = false;
        _image?.Dispose();
        base.OnFormClosed(e);
    }
}
