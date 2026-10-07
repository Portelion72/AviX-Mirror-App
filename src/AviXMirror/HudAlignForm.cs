using System.Drawing.Drawing2D;
using AviXMirror.Hud;
using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// « Aligner les flèches » (mode Capture, tous les jeux) : l'image du rétro s'affiche avec les flèches de
/// l'ATH ; on fige l'image, puis on fait glisser une flèche au-dessus de sa voiture. Le point de vue
/// (horizon, centre, champ de vision) est recalculé pour que toutes les flèches suivent. Avec deux voitures
/// ou plus à des endroits différents de l'image, le champ de vision est aussi calculé.
/// </summary>
public sealed class HudAlignForm : Form
{
    const float GrabRadius = 34;   // distance de prise d'une flèche, en pixels à l'écran
    const int CountdownSeconds = 5;

    readonly MirrorEngine _engine;
    readonly FrameBuffer _frames;
    readonly Settings _settings;
    readonly PictureBox _picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 8, 12, 0) };
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 100 };
    readonly Button _freeze = new() { Text = "Figer dans 5 s", AutoSize = true };
    readonly Button _live = new() { Text = "Reprendre le direct", AutoSize = true };

    Bitmap? _image;
    List<HudTarget> _targets = new();
    bool _frozen;
    int _freezeId;
    DateTime? _freezeAt;

    // Points posés par l'utilisateur : voiture (repère du joueur) -> position dans l'image (fraction 0..1).
    readonly List<(HudTarget Target, PointF Point, int FreezeId)> _matches = new();
    int _dragTarget = -1;
    PointF _dragPoint;  // pointe de la flèche déplacée (pixels image)
    PointF _dragOffset; // de la souris à la pointe, pour que la flèche ne saute pas sous la souris

    readonly double _startHorizon, _startCenter, _startFov;

    public double Horizon { get; private set; }
    public double Center { get; private set; }
    public double Fov { get; private set; }

    public HudAlignForm(MirrorEngine engine, Settings effective)
    {
        _engine = engine;
        _frames = engine.Frames;
        _settings = effective.Clone();
        Horizon = _startHorizon = effective.HudCaptureHorizon;
        Center = _startCenter = effective.HudCaptureCenter;
        Fov = _startFov = effective.HudCaptureFov;

        Text = "Aligner les flèches sur les voitures";
        Width = 1280;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Theme.Background;
        ForeColor = Ui.Theme.Text;

        _info.ForeColor = Ui.Theme.Text;
        _info.BackColor = Ui.Theme.Surface;
        _info.Font = Ui.Theme.Font(9.5f);

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
        var clear = new Button { Text = "Effacer les points", AutoSize = true };
        var reset = new Button { Text = "Valeurs de départ", AutoSize = true };
        _freeze.Click += (_, _) => { _freezeAt = DateTime.Now.AddSeconds(CountdownSeconds); UpdateInfo(); };
        _live.Click += (_, _) => { _frozen = false; _freezeAt = null; UpdateInfo(); };
        clear.Click += (_, _) => { _matches.Clear(); Refit(); };
        reset.Click += (_, _) =>
        {
            _matches.Clear();
            Horizon = _startHorizon;
            Center = _startCenter;
            Fov = _startFov;
            UpdateInfo();
            _picture.Invalidate();
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel, reset, clear, _live, _freeze });
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_picture);
        Controls.Add(_info);
        Controls.Add(buttons);

        _picture.Paint += OnPicturePaint;
        _picture.MouseDown += OnMouseDown;
        _picture.MouseMove += OnMouseMove;
        _picture.MouseUp += OnMouseUp;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space)
            {
                Freeze();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _refresh.Tick += (_, _) => Tick();
        _refresh.Start();
        UpdateInfo();
    }

    // ---------- Image et voitures ----------

    void Tick()
    {
        if (_freezeAt is { } at)
        {
            if (DateTime.Now >= at)
            {
                _freezeAt = null;
                Freeze();
                Activate(); // revient au premier plan après le délai passé en jeu
            }
            UpdateInfo();
        }
        if (!_frozen)
            Grab();
        _picture.Invalidate();
    }

    void Grab()
    {
        var snapshot = _frames.Snapshot();
        if (snapshot == null)
            return;
        _image?.Dispose();
        _image = snapshot;
        _targets = _engine.HudTargets() ?? new List<HudTarget>();
    }

    void Freeze()
    {
        Grab();
        _frozen = true;
        _freezeId++;
        UpdateInfo();
    }

    Settings Current()
    {
        _settings.HudCaptureHorizon = Horizon;
        _settings.HudCaptureCenter = Center;
        _settings.HudCaptureFov = Fov;
        return _settings;
    }

    HudProjection Projection(int w, int h) => HudProjection.ForCapture(Current(), w, h);

    /// <summary>Recalcule le point de vue à partir de tous les points posés.</summary>
    void Refit()
    {
        if (_image != null && _matches.Count > 0)
        {
            int w = _image.Width, h = _image.Height;
            var start = Projection(w, h);
            var pixels = _matches.Select(m => (m.Target, new PointF(m.Point.X * w, m.Point.Y * h))).ToList();
            var fit = start.Fit(pixels, HudProjection.FocalFor(60, h), HudProjection.FocalFor(2, h));
            Horizon = Math.Round(Math.Clamp(fit.Cy / h * 100, 0, 100), 1);
            Center = Math.Round(Math.Clamp(fit.Cx / w * 100, 0, 100), 1);
            Fov = Math.Round(Math.Clamp(HudProjection.FovFor(fit.Focal, h), 2, 60), 1);
        }
        UpdateInfo();
        _picture.Invalidate();
    }

    void UpdateInfo()
    {
        string step = _freezeAt is { } at
            ? $"Retournez en jeu : image figée dans {Math.Max(0, Math.Ceiling((at - DateTime.Now).TotalSeconds))} s…"
            : !_frozen
                ? "1. Roulez avec des voitures derrière vous (ou un replay), puis figez l'image : Espace, un clic sur une flèche, ou « Figer dans 5 s » pour avoir le temps de revenir en jeu."
                : "2. Faites glisser chaque flèche juste au-dessus du toit de sa voiture. Deux voitures éloignées l'une de l'autre donnent aussi le champ de vision. " +
                  "Vous pouvez reprendre le direct et figer une autre image pour ajouter des points.";
        _info.Text = step + "\n" +
                     $"Horizon {Horizon:0.0} %   ·   Centre {Center:0.0} %   ·   Champ de vision {Fov:0.0}°   ·   {_matches.Count} point(s) posé(s)";
        _live.Enabled = _frozen || _freezeAt != null;
        _freeze.Enabled = _freezeAt == null;
    }

    // ---------- Géométrie de l'affichage ----------

    RectangleF ImageRect()
    {
        if (_image == null)
            return RectangleF.Empty;
        var c = _picture.ClientSize;
        float scale = Math.Min((float)c.Width / _image.Width, (float)c.Height / _image.Height);
        float w = _image.Width * scale, h = _image.Height * scale;
        return new RectangleF((c.Width - w) / 2, (c.Height - h) / 2, w, h);
    }

    PointF ToScreen(PointF p)
    {
        var r = ImageRect();
        float k = _image == null ? 1 : r.Width / _image.Width;
        return new PointF(r.X + p.X * k, r.Y + p.Y * k);
    }

    PointF ToImage(Point p)
    {
        var r = ImageRect();
        float k = _image == null || r.Width <= 0 ? 1 : _image.Width / r.Width;
        return new PointF((p.X - r.X) * k, (p.Y - r.Y) * k);
    }

    /// <summary>Ancre de chaque voiture (pixels image), ou null hors de l'image.</summary>
    List<(int Index, PointF Anchor)> Anchors()
    {
        var list = new List<(int, PointF)>();
        if (_image == null)
            return list;
        int w = _image.Width, h = _image.Height;
        var projection = Projection(w, h);
        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (t.Lz <= 3)
                continue;
            if (projection.Anchor(t) is { } p && p.X > -w * 0.25f && p.X < w * 1.25f && p.Y > -h * 0.5f && p.Y < h * 1.5f)
                list.Add((i, p));
        }
        return list;
    }

    // ---------- Souris ----------

    int HitTest(Point location)
    {
        int best = -1;
        float bestDistance = GrabRadius;
        foreach (var (index, anchor) in Anchors())
        {
            var s = ToScreen(anchor);
            // La flèche est dessinée au-dessus de son ancre : on la prend un peu plus haut.
            float dx = location.X - s.X, dy = location.Y - (s.Y - 14);
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = index;
            }
        }
        return best;
    }

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _image == null)
            return;
        int hit = HitTest(e.Location);
        if (hit < 0)
            return;
        if (!_frozen)
        {
            // Un clic sur une flèche fige l'image telle qu'elle est.
            var target = _targets[hit];
            _frozen = true;
            _freezeId++;
            _freezeAt = null;
            hit = _targets.IndexOf(target);
            UpdateInfo();
        }
        _dragTarget = hit;
        var mouse = ToImage(e.Location);
        var anchor = Anchors().FirstOrDefault(a => a.Index == hit).Anchor;
        _dragOffset = new PointF(anchor.X - mouse.X, anchor.Y - mouse.Y);
        _dragPoint = anchor;
    }

    PointF DragPointAt(Point location)
    {
        var mouse = ToImage(location);
        return new PointF(mouse.X + _dragOffset.X, mouse.Y + _dragOffset.Y);
    }

    void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragTarget >= 0)
        {
            _dragPoint = DragPointAt(e.Location);
            _picture.Invalidate();
            return;
        }
        _picture.Cursor = HitTest(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
    }

    void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragTarget < 0 || _image == null)
            return;
        var target = _targets[_dragTarget];
        _dragTarget = -1;
        // Pointe de la flèche lâchée ici.
        var p = DragPointAt(e.Location);
        var point = new PointF(p.X / _image.Width, p.Y / _image.Height);
        _matches.RemoveAll(m => m.FreezeId == _freezeId && m.Target == target);
        _matches.Add((target, point, _freezeId));
        Refit();
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
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(_image, r);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var projection = Projection(_image.Width, _image.Height);
        float scale = r.Width / _image.Width;

        // Horizon.
        using (var pen = new Pen(Color.FromArgb(230, 255, 0, 200), 2) { DashStyle = DashStyle.Dash })
        {
            float y = r.Y + (float)projection.Cy * scale;
            g.DrawLine(pen, r.Left, y, r.Right, y);
            float x = r.X + (float)projection.Cx * scale;
            using var axis = new Pen(Color.FromArgb(120, 255, 0, 200), 1) { DashStyle = DashStyle.Dot };
            g.DrawLine(axis, x, r.Top, x, r.Bottom);
            using var font = Ui.Theme.Font(9f, FontStyle.Bold);
            TextRenderer.DrawText(g, "HORIZON", font, new Point((int)r.X + 6, (int)y - 20), Color.FromArgb(255, 0, 200));
        }

        // Points posés dans l'image figée.
        foreach (var m in _matches.Where(m => m.FreezeId == _freezeId && _frozen))
        {
            var s = ToScreen(new PointF(m.Point.X * _image.Width, m.Point.Y * _image.Height));
            using var ring = new Pen(Color.FromArgb(80, 230, 90), 2);
            g.DrawEllipse(ring, s.X - 7, s.Y - 7, 14, 14);
        }

        // Flèches de l'ATH (celle en cours de déplacement suit la souris).
        float unit = r.Height / 400f;
        using var small = Ui.Theme.Font(8f);
        foreach (var (index, anchor) in Anchors())
        {
            var t = _targets[index];
            bool matched = _frozen && _matches.Any(m => m.FreezeId == _freezeId && m.Target == t);
            var p = index == _dragTarget ? ToScreen(_dragPoint) : ToScreen(anchor);
            float size = (float)Math.Clamp(900 / t.Lz, 14, 44) * Math.Max(0.6f, unit);
            var color = index == _dragTarget ? Color.White : matched ? Color.FromArgb(80, 230, 90) : Ui.Theme.Accent;
            HudRenderer.DrawArrow(g, p.X, p.Y, size, color);
            TextRenderer.DrawText(g, $"{t.Lz:0} m", small, new Point((int)p.X + (int)(size / 2) + 2, (int)(p.Y - size)), Color.White);
        }

        using var big = Ui.Theme.Font(11f);
        using var bold = Ui.Theme.Font(9f, FontStyle.Bold);
        if (_targets.Count == 0)
            TextRenderer.DrawText(g, "Aucune voiture derrière vous dans la télémétrie.", big,
                new Rectangle((int)r.X, (int)r.Bottom - 40, (int)r.Width, 36), Color.White, TextFormatFlags.HorizontalCenter);
        if (_frozen)
            TextRenderer.DrawText(g, "IMAGE FIGÉE", bold, new Point((int)r.Right - 110, (int)r.Y + 6), Ui.Theme.Accent);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refresh.Stop();
        _refresh.Dispose();
        _image?.Dispose();
        base.OnFormClosed(e);
    }
}
