using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AviXMirror.Capture;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Affiche l'image capturée entière et permet de placer la zone du rétro à la souris.
/// Le cadre s'aimante, comme dans Word, sur des repères horizontaux et verticaux : bords et milieu
/// de l'image, bords du rétro virtuel détectés dans l'image et milieu entre deux bords.
/// Les points de couleur du cache se placent autour du cadre (double-clic pour ajouter, glisser
/// pour déplacer, clic droit pour supprimer).
/// </summary>
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
    const float SnapPixels = 8;   // distance d'aimantation, en pixels à l'écran
    const float PointRadius = 6;  // rayon d'un point de couleur à l'écran

    enum Drag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight, Sample }
    Drag _drag;
    int _dragSample = -1;
    PointF _dragOrigin;        // point de la souris au début (pixels image)
    RectangleF _dragStartRect; // rectangle au début du geste
    RectangleF _sel;           // sélection en pixels image (flottants)
    Rectangle _selection;      // en pixels de l'image source

    readonly Padding _margins;
    readonly List<MaskSample> _samples;

    // Repères d'alignement (pixels image) : ceux qui aimantent les bords, ceux qui aimantent le centre.
    readonly record struct Guide(float Position, bool Vertical, bool CenterOnly);
    List<Guide> _guides = new();
    readonly List<Guide> _activeGuides = new();
    DateTime _guidesTime = DateTime.MinValue;

    public Rectangle Selection => _selection;
    public List<MaskSample> Samples => _samples;

    public CalibrationForm(WgcCapture capture, FrameBuffer frames, Rectangle current, Padding maskMargins, IEnumerable<MaskSample> samples)
    {
        _capture = capture;
        _frames = frames;
        _selection = current;
        _margins = maskMargins;
        _samples = samples.Select(x => new MaskSample { Side = x.Side, Position = x.Position, Distance = x.Distance }).ToList();

        Text = "Zone du rétroviseur — déplacez le cadre, réduisez-le par les coins";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Theme.Background;
        ForeColor = Ui.Theme.Text;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
            BackColor = Ui.Theme.Surface,
        };
        var ok = new Button { Text = "Valider", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
        var full = new Button { Text = "Taille maximale", AutoSize = true };
        full.Click += (_, _) => { ResetSelection(); _picture.Invalidate(); };
        var hint = new Label
        {
            Text = "Le cadre s'aimante sur les repères (bords du rétro, milieux) — maintenez Alt pour désactiver.   " +
                   "Points de couleur du cache : double-clic autour du cadre pour ajouter, glisser pour déplacer, clic droit pour supprimer.",
            AutoSize = false,
            Width = 760,
            Height = 34,
            ForeColor = Ui.Theme.TextMuted,
            Padding = new Padding(0, 2, 12, 0),
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel, full, hint });
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_picture);
        Controls.Add(buttons);

        _picture.Paint += OnPicturePaint;
        _picture.MouseDown += OnMouseDown;
        _picture.MouseMove += OnMouseMove;
        _picture.MouseUp += (_, _) => { _drag = Drag.None; _activeGuides.Clear(); _picture.Invalidate(); };
        _picture.MouseDoubleClick += OnMouseDoubleClick;

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
        // Les repères suivent l'image (le rétro peut bouger) : recalculés toutes les 2 s, hors glissement.
        if (_drag == Drag.None && (DateTime.Now - _guidesTime).TotalSeconds > 2)
        {
            _guides = FindGuides(_image);
            _guidesTime = DateTime.Now;
        }
        _picture.Invalidate();
    }

    // ---------- Repères d'alignement ----------

    /// <summary>
    /// Repères : bords et milieux de l'image, plus les longs traits horizontaux et verticaux de l'image
    /// (cadre du rétro virtuel), et le milieu entre deux traits qui se font face.
    /// </summary>
    static List<Guide> FindGuides(Bitmap image)
    {
        int w = image.Width, h = image.Height;
        var guides = new List<Guide>
        {
            new(0, true, false), new(w, true, false), new(w / 2f, true, true),
            new(0, false, false), new(h, false, false), new(h / 2f, false, true),
        };
        try
        {
            var lum = Luminance(image);
            var vertical = Segments(lum, w, h, vertical: true);
            var horizontal = Segments(lum, w, h, vertical: false);
            guides.AddRange(vertical.Select(s => new Guide(s.Pos, true, false)));
            guides.AddRange(horizontal.Select(s => new Guide(s.Pos, false, false)));
            guides.AddRange(Centers(vertical).Select(c => new Guide(c, true, true)));
            guides.AddRange(Centers(horizontal).Select(c => new Guide(c, false, true)));
        }
        catch
        {
            // Image illisible : seulement les repères de l'image.
        }
        return guides;
    }

    static unsafe byte[] Luminance(Bitmap image)
    {
        int w = image.Width, h = image.Height;
        var lum = new byte[w * h];
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                uint* row = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                for (int x = 0; x < w; x++)
                {
                    uint c = row[x];
                    lum[y * w + x] = (byte)((((c >> 16) & 0xFF) * 77 + ((c >> 8) & 0xFF) * 150 + (c & 0xFF) * 29) >> 8);
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }
        return lum;
    }

    readonly record struct Segment(float Pos, int From, int To);

    /// <summary>
    /// Longs traits droits : pour chaque colonne (ou ligne), plus longue suite de pixels où la luminosité
    /// change nettement d'un côté à l'autre. On garde les plus longs, sans doublons voisins.
    /// </summary>
    static List<Segment> Segments(byte[] lum, int w, int h, bool vertical)
    {
        const int Contrast = 36;
        int lines = vertical ? w : h, length = vertical ? h : w;
        int minRun = Math.Max(40, length / 20);
        var found = new List<(int Pos, int Run, int From, int To)>();
        for (int i = 1; i < lines; i++)
        {
            int run = 0, best = 0, start = 0, bestFrom = 0, bestTo = 0, gap = 0;
            for (int j = 0; j < length; j++)
            {
                int a = vertical ? lum[j * w + i] : lum[i * w + j];
                int b = vertical ? lum[j * w + i - 1] : lum[(i - 1) * w + j];
                if (Math.Abs(a - b) >= Contrast)
                {
                    if (run == 0)
                        start = j;
                    run += 1 + gap;
                    gap = 0;
                    if (run > best)
                    {
                        best = run;
                        bestFrom = start;
                        bestTo = j;
                    }
                }
                else if (run > 0 && ++gap > 2)
                {
                    run = 0;
                    gap = 0;
                }
            }
            if (best >= minRun)
                found.Add((i, best, bestFrom, bestTo));
        }

        var kept = new List<Segment>();
        foreach (var f in found.OrderByDescending(f => f.Run))
        {
            if (kept.Any(k => Math.Abs(k.Pos - f.Pos) < 6))
                continue;
            kept.Add(new Segment(f.Pos, f.From, f.To));
            if (kept.Count >= 12)
                break;
        }
        return kept;
    }

    /// <summary>Milieux entre deux traits parallèles qui se font face (ex. bords gauche et droit du rétro).</summary>
    static IEnumerable<float> Centers(List<Segment> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        for (int j = i + 1; j < segments.Count; j++)
        {
            var a = segments[i];
            var b = segments[j];
            int overlap = Math.Min(a.To, b.To) - Math.Max(a.From, b.From);
            int shorter = Math.Min(a.To - a.From, b.To - b.From);
            if (Math.Abs(a.Pos - b.Pos) > 60 && overlap > shorter / 2)
                yield return (a.Pos + b.Pos) / 2;
        }
    }

    /// <summary>
    /// Aimante une valeur (bord ou centre) sur le repère le plus proche. Retourne le décalage à appliquer
    /// (0 si aucun repère assez proche) et note le repère utilisé.
    /// </summary>
    float Snap(float[] edges, float center, bool vertical, float threshold, out Guide? used)
    {
        used = null;
        float best = threshold;
        float delta = 0;
        foreach (var g in _guides)
        {
            if (g.Vertical != vertical)
                continue;
            var values = g.CenterOnly ? new[] { center } : edges;
            foreach (var v in values)
            {
                float d = g.Position - v;
                if (Math.Abs(d) < best)
                {
                    best = Math.Abs(d);
                    delta = d;
                    used = g;
                }
            }
        }
        return delta;
    }

    // ---------- Géométrie ----------

    RectangleF ImageRect()
    {
        if (_image == null)
            return RectangleF.Empty;
        var c = _picture.ClientSize;
        float scale = Math.Min((float)c.Width / _image.Width, (float)c.Height / _image.Height);
        float w = _image.Width * scale, h = _image.Height * scale;
        return new RectangleF((c.Width - w) / 2, (c.Height - h) / 2, w, h);
    }

    float ImageScale => _image == null || ImageRect().Width <= 0 ? 1 : ImageRect().Width / _image.Width;

    PointF ToImage(Point p)
    {
        var r = ImageRect();
        if (_image == null || r.Width <= 0)
            return PointF.Empty;
        float k = _image.Width / r.Width;
        return new PointF((p.X - r.X) * k, (p.Y - r.Y) * k);
    }

    PointF ToScreen(PointF p)
    {
        var r = ImageRect();
        return new PointF(r.X + p.X * ImageScale, r.Y + p.Y * ImageScale);
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
        float k = ImageScale;
        return new RectangleF(r.X + _sel.X * k, r.Y + _sel.Y * k, _sel.Width * k, _sel.Height * k);
    }

    /// <summary>Cache (zone + marges), en pixels image.</summary>
    Rectangle MaskRect() => new(_selection.X - _margins.Left, _selection.Y - _margins.Top,
        _selection.Width + _margins.Horizontal, _selection.Height + _margins.Vertical);

    PointF SampleOnScreen(MaskSample sample) => ToScreen(MaskForm.ScreenPoint(sample, MaskRect()));

    /// <summary>Point de couleur au plus près d'un point de l'image : côté le plus proche du cache.</summary>
    MaskSample SampleAt(PointF p)
    {
        var r = MaskRect();
        float dTop = Math.Abs(p.Y - r.Top), dBottom = Math.Abs(p.Y - r.Bottom), dLeft = Math.Abs(p.X - r.Left), dRight = Math.Abs(p.X - r.Right);
        float min = Math.Min(Math.Min(dTop, dBottom), Math.Min(dLeft, dRight));
        MaskSide side = min == dTop ? MaskSide.Haut : min == dBottom ? MaskSide.Bas : min == dLeft ? MaskSide.Gauche : MaskSide.Droite;
        bool horizontal = side is MaskSide.Haut or MaskSide.Bas;
        double position = horizontal
            ? (p.X - r.Left) / Math.Max(1.0, r.Width - 1) * 100
            : (p.Y - r.Top) / Math.Max(1.0, r.Height - 1) * 100;
        float outside = side switch
        {
            MaskSide.Haut => r.Top - p.Y,
            MaskSide.Bas => p.Y - (r.Bottom - 1),
            MaskSide.Gauche => r.Left - p.X,
            _ => p.X - (r.Right - 1),
        };
        return new MaskSample { Side = side, Position = Math.Round(Math.Clamp(position, 0, 100), 1), Distance = Math.Max(1, (int)Math.Round(outside)) };
    }

    int SampleHit(Point p)
    {
        for (int i = _samples.Count - 1; i >= 0; i--)
        {
            var s = SampleOnScreen(_samples[i]);
            if (Math.Abs(p.X - s.X) <= PointRadius + 3 && Math.Abs(p.Y - s.Y) <= PointRadius + 3)
                return i;
        }
        return -1;
    }

    Drag HitTest(Point p)
    {
        if (SampleHit(p) >= 0)
            return Drag.Sample;
        var sel = SelectionOnScreen();
        bool Near(float x, float y) => Math.Abs(p.X - x) <= HandleSize && Math.Abs(p.Y - y) <= HandleSize;
        if (Near(sel.Left, sel.Top)) return Drag.TopLeft;
        if (Near(sel.Right, sel.Top)) return Drag.TopRight;
        if (Near(sel.Left, sel.Bottom)) return Drag.BottomLeft;
        if (Near(sel.Right, sel.Bottom)) return Drag.BottomRight;
        return sel.Contains(p) ? Drag.Move : Drag.None;
    }

    // ---------- Souris ----------

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (_image == null)
            return;
        if (e.Button == MouseButtons.Right)
        {
            // Clic droit sur un point : suppression (il en reste toujours au moins un).
            int hit = SampleHit(e.Location);
            if (hit >= 0 && _samples.Count > 1)
            {
                _samples.RemoveAt(hit);
                _picture.Invalidate();
            }
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;
        _drag = HitTest(e.Location);
        _dragSample = _drag == Drag.Sample ? SampleHit(e.Location) : -1;
        _dragOrigin = ToImage(e.Location);
        _dragStartRect = _sel;
    }

    void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (_image == null || e.Button != MouseButtons.Left || SelectionOnScreen().Contains(e.Location) || SampleHit(e.Location) >= 0)
            return;
        _samples.Add(SampleAt(ToImage(e.Location)));
        _picture.Invalidate();
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
                Drag.Sample => Cursors.Hand,
                _ => Cursors.Default,
            };
            return;
        }

        var p = ToImage(e.Location);
        if (_drag == Drag.Sample)
        {
            if (_dragSample >= 0 && _dragSample < _samples.Count)
                _samples[_dragSample] = SampleAt(p);
            _picture.Invalidate();
            return;
        }

        float imgW = _image.Width, imgH = _image.Height;
        var start = _dragStartRect;
        bool snap = (ModifierKeys & Keys.Alt) == 0;
        float threshold = SnapPixels / ImageScale;
        _activeGuides.Clear();

        if (_drag == Drag.Move)
        {
            float x = start.X + p.X - _dragOrigin.X;
            float y = start.Y + p.Y - _dragOrigin.Y;
            if (snap)
            {
                x += Snap(new[] { x, x + start.Width }, x + start.Width / 2, vertical: true, threshold, out var gx);
                y += Snap(new[] { y, y + start.Height }, y + start.Height / 2, vertical: false, threshold, out var gy);
                if (gx is { } vx) _activeGuides.Add(vx);
                if (gy is { } hy) _activeGuides.Add(hy);
            }
            x = Math.Clamp(x, 0, imgW - start.Width);
            y = Math.Clamp(y, 0, imgH - start.Height);
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

            if (snap)
            {
                // Le coin mobile s'aimante sur un repère vertical (largeur) ou horizontal (hauteur).
                float cornerX = left ? anchorX - width : anchorX + width;
                float cornerY = top ? anchorY - width / Aspect : anchorY + width / Aspect;
                float dx = Snap(new[] { cornerX }, float.NaN, vertical: true, threshold, out var gx);
                float dy = Snap(new[] { cornerY }, float.NaN, vertical: false, threshold, out var gy);
                if (gx is { } vx && (gy is null || Math.Abs(dx) <= Math.Abs(dy) * Aspect))
                {
                    width += left ? -dx : dx;
                    _activeGuides.Add(vx);
                }
                else if (gy is { } hy)
                {
                    width += (top ? -dy : dy) * Aspect;
                    _activeGuides.Add(hy);
                }
            }

            float maxW = left ? anchorX : imgW - anchorX;
            float maxH = top ? anchorY : imgH - anchorY;
            width = Math.Clamp(width, 64, Math.Min(maxW, maxH * Aspect));
            float height = width / Aspect;
            _sel = new RectangleF(left ? anchorX - width : anchorX, top ? anchorY - height : anchorY, width, height);
        }
        SyncSelection();
        _picture.Invalidate();
    }

    // ---------- Dessin ----------

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
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_selection.Width <= 0 || _selection.Height <= 0)
            return;

        float scale = ImageScale;
        var sel = new RectangleF(r.X + _selection.X * scale, r.Y + _selection.Y * scale,
            _selection.Width * scale, _selection.Height * scale);
        using (var shade = new Region(r))
        {
            shade.Exclude(sel);
            using var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            g.FillRegion(dim, shade);
        }

        // Repères actifs : ligne pointillée sur toute l'image, comme dans Word.
        using (var guidePen = new Pen(Color.FromArgb(230, 255, 0, 200), 1.5f) { DashStyle = DashStyle.Dash })
        {
            foreach (var guide in _activeGuides)
            {
                if (guide.Vertical)
                {
                    float x = r.X + guide.Position * scale;
                    g.DrawLine(guidePen, x, r.Top, x, r.Bottom);
                }
                else
                {
                    float y = r.Y + guide.Position * scale;
                    g.DrawLine(guidePen, r.Left, y, r.Right, y);
                }
            }
        }

        // Contour du cache (zone + marges), s'il est plus grand que la zone.
        var mask = MaskRect();
        if (mask != _selection)
        {
            using var maskPen = new Pen(Color.FromArgb(160, Ui.Theme.Accent), 1) { DashStyle = DashStyle.Dot };
            var tl = ToScreen(new PointF(mask.X, mask.Y));
            g.DrawRectangle(maskPen, tl.X, tl.Y, mask.Width * scale, mask.Height * scale);
        }

        using (var pen = new Pen(Ui.Theme.Accent, 2))
            g.DrawRectangle(pen, sel.X, sel.Y, sel.Width, sel.Height);
        using (var handle = new SolidBrush(Ui.Theme.Accent))
        {
            foreach (var (hx, hy) in new[] { (sel.Left, sel.Top), (sel.Right, sel.Top), (sel.Left, sel.Bottom), (sel.Right, sel.Bottom) })
                g.FillRectangle(handle, hx - HandleSize / 2f, hy - HandleSize / 2f, HandleSize, HandleSize);
        }

        // Points de couleur du cache : pastille de la couleur lue dans l'image, contour blanc.
        using (var ring = new Pen(Color.White, 2))
        using (var ringDark = new Pen(Color.Black, 1))
        {
            foreach (var sample in _samples)
            {
                var ip = MaskForm.ScreenPoint(sample, mask);
                var sp = ToScreen(ip);
                var color = ip.X >= 0 && ip.Y >= 0 && ip.X < _image.Width && ip.Y < _image.Height ? _image.GetPixel(ip.X, ip.Y) : Color.Black;
                using var fill = new SolidBrush(color);
                var circle = new RectangleF(sp.X - PointRadius, sp.Y - PointRadius, PointRadius * 2, PointRadius * 2);
                g.FillEllipse(fill, circle);
                g.DrawEllipse(ring, circle);
                g.DrawEllipse(ringDark, RectangleF.Inflate(circle, 1.5f, 1.5f));
            }
        }

        var text = $"{_selection.Width}x{_selection.Height} @ {_selection.X},{_selection.Y}   ·   {_samples.Count} point(s) de couleur";
        TextRenderer.DrawText(g, text, Font, new Point((int)sel.X + 4, (int)sel.Y + 4), Ui.Theme.Accent);
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
