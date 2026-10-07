using System.ComponentModel;
using System.Drawing.Design;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Design;

namespace AviXMirror
{
    /// <summary>Bornes et pas d'un réglage numérique, affiché sous forme de jauge.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class GaugeAttribute : Attribute
    {
        public GaugeAttribute(double min, double max, double step)
        {
            Min = min;
            Max = max;
            Step = step;
        }

        public double Min { get; }
        public double Max { get; }
        public double Step { get; }
    }
}

namespace AviXMirror.Ui
{
    /// <summary>
    /// Éditeur « jauge » des réglages numériques : mini-jauge dans la case de la valeur et, au clic
    /// sur la flèche, un curseur à faire glisser (aperçu en direct, Entrée ou clic ailleurs pour valider).
    /// </summary>
    public sealed class GaugeEditor : UITypeEditor
    {
        /// <summary>Valeur en cours de réglage (aperçu en direct) : nom du réglage, valeur.</summary>
        public static event Action<string, object>? Preview;

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

        static GaugeAttribute Range(ITypeDescriptorContext? context) =>
            context?.PropertyDescriptor?.Attributes.OfType<GaugeAttribute>().FirstOrDefault() ?? new GaugeAttribute(0, 100, 1);

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            if (provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service || value == null)
                return value;
            var type = value.GetType();
            var range = Range(context);
            var name = context?.PropertyDescriptor?.Name ?? "";
            using var gauge = new GaugeControl(range, Convert.ToDouble(value), context?.PropertyDescriptor?.DisplayName ?? "");
            gauge.ValueChanged += v => Preview?.Invoke(name, Convert.ChangeType(v, type));
            gauge.Done += () => service.CloseDropDown();
            service.DropDownControl(gauge);
            return Convert.ChangeType(type == typeof(int) ? Math.Round(gauge.Value) : gauge.Value, type);
        }

        public override void PaintValue(PaintValueEventArgs e)
        {
            var range = Range(e.Context);
            double v = e.Value == null ? 0 : Convert.ToDouble(e.Value);
            double t = Math.Clamp((v - range.Min) / Math.Max(1e-9, range.Max - range.Min), 0, 1);
            var r = e.Bounds;
            using var track = new SolidBrush(Theme.SurfaceRaised);
            using var fill = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(track, r);
            e.Graphics.FillRectangle(fill, r.X, r.Y, (float)(r.Width * t), r.Height);
        }
    }

    /// <summary>Curseur aux couleurs AVIX (glisser, molette, flèches ; Entrée pour valider).</summary>
    public sealed class GaugeControl : Control
    {
        readonly GaugeAttribute _range;
        readonly string _title;
        double _value;
        bool _dragging;

        public event Action<double>? ValueChanged;
        public event Action? Done;

        public double Value => _value;

        public GaugeControl(GaugeAttribute range, double value, string title)
        {
            _range = range;
            _title = title;
            _value = Snap(value);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(320, 86);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        double Snap(double v)
        {
            v = Math.Clamp(v, _range.Min, _range.Max);
            if (_range.Step > 0)
                v = _range.Min + Math.Round((v - _range.Min) / _range.Step) * _range.Step;
            return Math.Round(Math.Clamp(v, _range.Min, _range.Max), 4);
        }

        void SetValue(double v)
        {
            v = Snap(v);
            if (v == _value)
                return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(v);
        }

        RectangleF Track => new(16, 52, Width - 32, 8);

        double ValueAt(int x)
        {
            var t = Track;
            return _range.Min + Math.Clamp((x - t.X) / t.Width, 0, 1) * (_range.Max - _range.Min);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _dragging = true;
            SetValue(ValueAt(e.X));
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
                SetValue(ValueAt(e.X));
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SetValue(_value + Math.Sign(e.Delta) * _range.Step);
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            double big = Math.Max(_range.Step, (_range.Max - _range.Min) / 20);
            switch (e.KeyCode)
            {
                case Keys.Left or Keys.Down: SetValue(_value - _range.Step); break;
                case Keys.Right or Keys.Up: SetValue(_value + _range.Step); break;
                case Keys.PageDown: SetValue(_value - big); break;
                case Keys.PageUp: SetValue(_value + big); break;
                case Keys.Home: SetValue(_range.Min); break;
                case Keys.End: SetValue(_range.Max); break;
                case Keys.Enter: Done?.Invoke(); break;
            }
            base.OnKeyDown(e);
        }

        string Format(double v) => _range.Step >= 1 ? v.ToString("0") : _range.Step >= 0.1 ? v.ToString("0.0") : v.ToString("0.00");

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Surface);
            using (var border = new Pen(Theme.Accent))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using var titleFont = Theme.Font(8.5f);
            using var valueFont = Theme.Font(15f, FontStyle.Bold);
            using var small = Theme.Font(7.5f);
            TextRenderer.DrawText(g, _title, titleFont, new Rectangle(14, 8, Width - 120, 20), Theme.TextMuted, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Format(_value), valueFont, new Rectangle(Width - 110, 4, 96, 34), Theme.Text, TextFormatFlags.Right);

            var t = Track;
            double frac = (_value - _range.Min) / Math.Max(1e-9, _range.Max - _range.Min);
            using (var trackPath = Theme.RoundedRect(t, 4))
            using (var trackBrush = new SolidBrush(Theme.SurfaceRaised))
                g.FillPath(trackBrush, trackPath);
            var filled = new RectangleF(t.X, t.Y, (float)(t.Width * frac), t.Height);
            if (filled.Width > 1)
            {
                using var fillPath = Theme.RoundedRect(filled, 4);
                using var fillBrush = new SolidBrush(Theme.Accent);
                g.FillPath(fillBrush, fillPath);
            }
            // Graduations tous les dixièmes.
            using (var tick = new Pen(Theme.Border))
                for (int i = 0; i <= 10; i++)
                {
                    float x = t.X + t.Width * i / 10f;
                    g.DrawLine(tick, x, t.Bottom + 4, x, t.Bottom + (i % 5 == 0 ? 10 : 7));
                }
            float knobX = t.X + (float)(t.Width * frac);
            using (var knob = new SolidBrush(Color.White))
            using (var knobPen = new Pen(Theme.Accent, 2))
            {
                g.FillEllipse(knob, knobX - 8, t.Y - 5, 16, 16);
                g.DrawEllipse(knobPen, knobX - 8, t.Y - 5, 16, 16);
            }
            TextRenderer.DrawText(g, Format(_range.Min), small, new Point((int)t.X - 4, (int)t.Bottom + 10), Theme.TextMuted);
            var maxText = Format(_range.Max);
            int maxWidth = TextRenderer.MeasureText(maxText, small).Width;
            TextRenderer.DrawText(g, maxText, small, new Point((int)t.Right - maxWidth + 4, (int)t.Bottom + 10), Theme.TextMuted);
        }
    }

    /// <summary>Couleur au format #RRVVBB : pastille dans la case et sélecteur de couleur Windows.</summary>
    public sealed class ColorHexEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

        static Color Parse(object? value)
        {
            try { return ColorTranslator.FromHtml(value as string ?? ""); }
            catch { return Color.Black; }
        }

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            using var dialog = new ColorDialog { Color = Parse(value), FullOpen = true, AnyColor = true };
            return dialog.ShowDialog() == DialogResult.OK
                ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
                : value;
        }

        public override void PaintValue(PaintValueEventArgs e)
        {
            using var brush = new SolidBrush(Parse(e.Value));
            e.Graphics.FillRectangle(brush, e.Bounds);
        }
    }

    /// <summary>Choix d'une image ou d'une animation (GIF) pour l'écran de veille.</summary>
    public sealed class ImageFileEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Image ou animation de l'écran de veille",
                Filter = "Images et animations (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous les fichiers|*.*",
            };
            var current = value as string;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                dialog.FileName = current;
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : value;
        }
    }
}
