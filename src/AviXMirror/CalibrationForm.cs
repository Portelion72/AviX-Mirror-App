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
    // Rectangle au format du VoCore (1280 x 400) : déplacement à l'intérieur, redimensionnement
    // uniquement par les coins (en diagonale, format conservé).
    const float Aspect = 1280f / 400f;
    const int HandleSize = 12;
    enum Drag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }
    Drag _drag;
    PointF _dragOrigin;        // point de la souris au début (pixels image)
    RectangleF _dragStartRect; // rectangle au début du geste
    RectangleF _sel;           // sélection en pixels image (flottants)
    Rectangle _selection; // en pixels de l'image source

    public Rectangle Selection => _selection;

    public CalibrationForm(WgcCapture capture, FrameBuffer frames, Rectangle current)
    {
        _capture = capture;
        _frames = frames;
        _selection = current;

        Text = "Zone du rétroviseur — déplacez le cadre, réduisez-le par les coins";
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
        full.Text = "Taille maximale";
        full.Click += (_, _) => { ResetSelection(); _picture.Invalidate(); };
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
        _picture.MouseDown += OnMouseDown;
        _picture.MouseMove += OnMouseMove;
        _picture.MouseUp += (_, _) => _drag = Drag.None;

        _capture.FullFrame = true;
        _refresh.Tick += (_, _) => RefreshImage();
        _refresh.Start();
    }

    void RefreshImage()
    {
        var snapshot = _frames.Snapshot();
        if (snapshot == null)
            return;
        bool first = _image == null;
        _image?.Dispose();
        _image = snapshot;
        if (first)
        {
            // Reprend la zone actuelle si elle existe, sinon le plus grand rectangle possible.
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                float w = Math.Min(_selection.Width, _image.Width), h = w / Aspect;
                _sel = new RectangleF(Math.Clamp(_selection.X, 0, _image.Width - w), Math.Clamp(_selection.Y, 0, Math.Max(0, _image.Height - h)), w, h);
                SyncSelection();
            }
            else
            {
                ResetSelection();
            }
        }
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

    PointF ToImage(Point p)
    {
        var r = ImageRect();
        if (_image == null || r.Width <= 0)
            return PointF.Empty;
        float scale = _image.Width / r.Width;
        return new PointF((p.X - r.X) * scale, (p.Y - r.Y) * scale);
    }

    /// <summary>Plus grand rectangle au format du VoCore, centré dans l'image.</summary>
    void ResetSelection()
    {
        if (_image == null)
            return;
        float w = _image.Width, h = w / Aspect;
        if (h > _image.Height) { h = _image.Height; w = h * Aspect; }
        _sel = new RectangleF((_image.Width - w) / 2, (_image.Height - h) / 2, w, h);
        SyncSelection();
    }

    void SyncSelection() => _selection = Rectangle.Round(_sel);

    RectangleF SelectionOnScreen()
    {
        var r = ImageRect();
        if (_image == null || r.Width <= 0)
            return RectangleF.Empty;
        float k = r.Width / _image.Width;
        return new RectangleF(r.X + _sel.X * k, r.Y + _sel.Y * k, _sel.Width * k, _sel.Height * k);
    }

    Drag HitTest(Point p)
    {
        var sel = SelectionOnScreen();
        bool Near(float x, float y) => Math.Abs(p.X - x) <= HandleSize && Math.Abs(p.Y - y) <= HandleSize;
        if (Near(sel.Left, sel.Top)) return Drag.TopLeft;
        if (Near(sel.Right, sel.Top)) return Drag.TopRight;
        if (Near(sel.Left, sel.Bottom)) return Drag.BottomLeft;
        if (Near(sel.Right, sel.Bottom)) return Drag.BottomRight;
        return sel.Contains(p) ? Drag.Move : Drag.None;
    }

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _image == null)
            return;
        _drag = HitTest(e.Location);
        _dragOrigin = ToImage(e.Location);
        _dragStartRect = _sel;
    }

    void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_image == null)
            return;
        if (_drag == Drag.None)
        {
            _picture.Cursor = HitTest(e.Location) switch
            {
                Drag.TopLeft or Drag.BottomRight => Cursors.SizeNWSE,
                Drag.TopRight or Drag.BottomLeft => Cursors.SizeNESW,
                Drag.Move => Cursors.SizeAll,
                _ => Cursors.Default,
            };
            return;
        }

        var p = ToImage(e.Location);
        float imgW = _image.Width, imgH = _image.Height;
        var start = _dragStartRect;

        if (_drag == Drag.Move)
        {
            float x = Math.Clamp(start.X + p.X - _dragOrigin.X, 0, imgW - start.Width);
            float y = Math.Clamp(start.Y + p.Y - _dragOrigin.Y, 0, imgH - start.Height);
            _sel = new RectangleF(x, y, start.Width, start.Height);
        }
        else
        {
            // Le coin opposé reste fixe ; la largeur suit la souris, la hauteur suit le format.
            bool left = _drag is Drag.TopLeft or Drag.BottomLeft;
            bool top = _drag is Drag.TopLeft or Drag.TopRight;
            float anchorX = left ? start.Right : start.Left;
            float anchorY = top ? start.Bottom : start.Top;
            float width = Math.Max(Math.Abs(p.X - anchorX), Math.Abs(p.Y - anchorY) * Aspect);
            float maxW = left ? anchorX : imgW - anchorX;
            float maxH = top ? anchorY : imgH - anchorY;
            width = Math.Clamp(width, 64, Math.Min(maxW, maxH * Aspect));
            float height = width / Aspect;
            _sel = new RectangleF(left ? anchorX - width : anchorX, top ? anchorY - height : anchorY, width, height);
        }
        SyncSelection();
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
            using var pen = new Pen(Ui.Theme.Accent, 2);
            g.DrawRectangle(pen, sel.X, sel.Y, sel.Width, sel.Height);
            using var handle = new SolidBrush(Ui.Theme.Accent);
            foreach (var (hx, hy) in new[] { (sel.Left, sel.Top), (sel.Right, sel.Top), (sel.Left, sel.Bottom), (sel.Right, sel.Bottom) })
                g.FillRectangle(handle, hx - HandleSize / 2f, hy - HandleSize / 2f, HandleSize, HandleSize);
            var text = $"{_selection.Width}x{_selection.Height} @ {_selection.X},{_selection.Y}";
            TextRenderer.DrawText(g, text, Font, new Point((int)sel.X + 4, (int)sel.Y + 4), Ui.Theme.Accent);
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
