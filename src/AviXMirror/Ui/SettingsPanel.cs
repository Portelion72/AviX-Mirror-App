using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace AviXMirror
{
    /// <summary>Réglage affiché seulement dans certains modes (ex. réglages de l'ATH propres à la capture).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ModeOnlyAttribute : Attribute
    {
        public ModeOnlyAttribute(params MirrorMode[] modes) => Modes = modes;
        public MirrorMode[] Modes { get; }
    }
}

namespace AviXMirror.Ui
{
    /// <summary>
    /// Réglages d'un onglet, une ligne par réglage : nom à gauche, valeur à droite.
    /// Valeurs numériques : curseur toujours visible + case numérique, modifiables tous les deux.
    /// Interrupteurs pour les oui/non, listes déroulantes pour les choix, pastille pour les couleurs.
    /// L'explication du réglage survolé s'affiche en bas.
    /// </summary>
    public sealed class SettingsPanel : Panel
    {
        readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        readonly TableLayoutPanel _table = new()
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(0, 0),
            Padding = new Padding(10, 6, 10, 6),
        };
        readonly Label _help = new() { Dock = DockStyle.Bottom, Height = 62, AutoSize = false, Padding = new Padding(12, 8, 12, 6) };
        readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 600 };
        Settings? _view;
        MirrorMode _mode;
        bool _loading;

        /// <summary>
        /// Valeur changée : nom du réglage, nouvelle valeur, et vrai quand elle est définitive (faux pendant
        /// qu'on fait glisser un curseur : aperçu en direct).
        /// </summary>
        public event Action<string, object?, bool>? ValueChanged;

        public SettingsPanel()
        {
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _scroll.Controls.Add(_table);
            _scroll.Resize += (_, _) => FitWidth();
            Controls.Add(_scroll);
            Controls.Add(_help);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            _scroll.BackColor = Theme.Surface;
            _table.BackColor = Theme.Surface;
            _help.BackColor = Theme.SurfaceRaised;
            _help.ForeColor = Theme.TextMuted;
            _help.Font = Theme.Font(8.5f);
        }

        void FitWidth()
        {
            int width = _scroll.ClientSize.Width;
            _table.MinimumSize = new Size(Math.Max(300, width), 0);
            _table.MaximumSize = new Size(Math.Max(300, width), 0);
        }

        /// <summary>Réglages de l'onglet <paramref name="tab"/> pour le mode <paramref name="mode"/>.</summary>
        public static IEnumerable<PropertyInfo> PropertiesOf(string tab, MirrorMode mode) =>
            typeof(Settings).GetProperties()
                .OrderBy(p => p.MetadataToken)
                .Where(p => p.CanWrite && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .Where(p => p.GetCustomAttribute<CategoryAttribute>()?.Category == tab)
                .Where(p => p.GetCustomAttribute<ModeOnlyAttribute>() is not { } only || only.Modes.Contains(mode));

        /// <summary>Construit les lignes de l'onglet à partir des valeurs de <paramref name="view"/>.</summary>
        public void Build(Settings view, string tab, MirrorMode mode)
        {
            _mode = mode;
            _view = view;
            _loading = true;
            var scroll = _scroll.AutoScrollPosition;
            bool sameTab = (string?)Tag == tab;
            Tag = tab;
            SuspendLayout();
            _table.SuspendLayout();
            var old = _table.Controls.Cast<Control>().ToArray();
            _table.Controls.Clear();
            foreach (var c in old)
                c.Dispose();
            _table.RowStyles.Clear();
            _table.RowCount = 0;
            _tips.RemoveAll();

            foreach (var property in PropertiesOf(tab, mode))
                AddRow(property);

            _help.Text = "Survolez un réglage pour afficher son explication.";
            _table.ResumeLayout();
            FitWidth();
            ResumeLayout();
            if (sameTab)
                _scroll.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            _loading = false;
        }

        void AddRow(PropertyInfo property)
        {
            string name = property.Name;
            string title = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? name;
            string help = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var value = property.GetValue(_view);

            var label = new Label
            {
                Text = title,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text,
                Font = Theme.Font(9f),
                Margin = new Padding(0, 2, 8, 2),
                AutoEllipsis = true,
            };
            var editor = CreateEditor(property, value);
            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(0, 3, 0, 3);

            int row = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(34, editor.Height + 6)));
            _table.Controls.Add(label, 0, row);
            _table.Controls.Add(editor, 1, row);

            string text = string.IsNullOrEmpty(help) ? title : title + " — " + help;
            void ShowHelp(object? sender, EventArgs e) => _help.Text = text;
            foreach (var c in new[] { label, editor }.Concat(Descendants(editor)))
            {
                c.MouseEnter += ShowHelp;
                c.GotFocus += ShowHelp;
            }
            if (!string.IsNullOrEmpty(help))
                _tips.SetToolTip(label, help);
        }

        static IEnumerable<Control> Descendants(Control c) =>
            c.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

        void Emit(string name, object? value, bool final)
        {
            if (_loading || _view == null)
                return;
            typeof(Settings).GetProperty(name)?.SetValue(_view, value);
            ValueChanged?.Invoke(name, value, final);
        }

        // ---------- Éditeurs ----------

        Control CreateEditor(PropertyInfo property, object? value)
        {
            var type = property.PropertyType;
            string name = property.Name;
            var editorType = property.GetCustomAttribute<EditorAttribute>()?.EditorTypeName ?? "";

            if (type == typeof(bool))
            {
                var toggle = new ToggleSwitch { Checked = value is true, Height = 26 };
                toggle.CheckedChanged += () => Emit(name, toggle.Checked, true);
                return Host(toggle, 52);
            }

            if (type.IsEnum)
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.SurfaceRaised,
                    ForeColor = Theme.Text,
                    Font = Theme.Font(9f),
                };
                foreach (var v in Enum.GetValues(type))
                    combo.Items.Add(v);
                combo.SelectedItem = value;
                combo.SelectedIndexChanged += (_, _) => Emit(name, combo.SelectedItem, true);
                return Host(combo, 260);
            }

            if (type == typeof(int) || type == typeof(double))
            {
                var gauge = property.GetCustomAttribute<GaugeAttribute>();
                return gauge != null
                    ? new SliderBox(gauge, Convert.ToDouble(value), type == typeof(int), (v, final) => Emit(name, Convert.ChangeType(v, type), final))
                    : NumberOnly(name, type, value);
            }

            if (type == typeof(string) && editorType.Contains(nameof(ColorHexEditor)))
                return ColorEditor(name, value as string ?? "");

            if (type == typeof(string) && editorType.Contains(nameof(ImageFileEditor)))
                return FileEditor(name, value as string ?? "");

            if (type == typeof(string))
            {
                var box = TextBox(value as string ?? "");
                box.Leave += (_, _) => Emit(name, box.Text, true);
                box.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        Emit(name, box.Text, true);
                        e.SuppressKeyPress = true;
                    }
                };
                return Host(box, 260);
            }

            if (type == typeof(List<LedClassColor>))
                return ClassColorsEditor(name, value as List<LedClassColor> ?? new());

            if (typeof(System.Collections.IList).IsAssignableFrom(type))
                return ListEditor(property, value as System.Collections.IList);

            return Host(new Label { Text = value?.ToString() ?? "", ForeColor = Theme.TextMuted, AutoSize = true }, 260);
        }

        /// <summary>Contrôle à largeur fixe, aligné à gauche dans la colonne des valeurs.</summary>
        static Control Host(Control control, int width)
        {
            var host = new Panel { Height = Math.Max(28, control.Height), BackColor = Color.Transparent };
            control.Width = width;
            control.Location = new Point(0, Math.Max(0, (host.Height - control.Height) / 2));
            control.Anchor = AnchorStyles.Left;
            host.Controls.Add(control);
            return host;
        }

        static TextBox TextBox(string text) => new()
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.SurfaceRaised,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f),
        };

        Control NumberOnly(string name, Type type, object? value)
        {
            bool integer = type == typeof(int);
            var number = new NumericUpDown
            {
                Minimum = -1_000_000,
                Maximum = 1_000_000,
                DecimalPlaces = integer ? 0 : 2,
                Increment = integer ? 1 : 0.1m,
                Value = Convert.ToDecimal(value ?? 0),
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Font(9.5f),
                TextAlign = HorizontalAlignment.Right,
            };
            number.ValueChanged += (_, _) => Emit(name, Convert.ChangeType(number.Value, type, CultureInfo.InvariantCulture), true);
            return Host(number, 120);
        }

        Control ColorEditor(string name, string hex)
        {
            var panel = new Panel { Height = 28, BackColor = Color.Transparent };
            var swatch = new FlatButton { Width = 44, Height = 26, Location = new Point(0, 1), FillOverride = Parse(hex) };
            var box = TextBox(hex);
            box.Width = 100;
            box.Location = new Point(52, 2);
            void Commit(string text)
            {
                swatch.FillOverride = Parse(text);
                swatch.Invalidate();
                Emit(name, text, true);
            }
            swatch.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { Color = Parse(box.Text), FullOpen = true, AnyColor = true };
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;
                box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                Commit(box.Text);
            };
            box.Leave += (_, _) => Commit(box.Text);
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Commit(box.Text);
                    e.SuppressKeyPress = true;
                }
            };
            panel.Controls.Add(swatch);
            panel.Controls.Add(box);
            return panel;
        }

        static Color Parse(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch { return Color.Black; }
        }

        Control FileEditor(string name, string path)
        {
            var panel = new Panel { Height = 28, BackColor = Color.Transparent };
            var box = TextBox(path);
            box.ReadOnly = true;
            box.Location = new Point(0, 2);
            box.Width = 220;
            var browse = new FlatButton { Text = "Choisir…", Width = 86, Height = 26, Location = new Point(228, 1) };
            var clear = new FlatButton { Text = "Aucune", Width = 76, Height = 26, Location = new Point(320, 1) };
            browse.Click += (_, _) =>
            {
                var chosen = new ImageFileEditor().EditValue(null, null!, box.Text) as string;
                if (chosen != null && chosen != box.Text)
                {
                    box.Text = chosen;
                    Emit(name, chosen, true);
                }
            };
            clear.Click += (_, _) =>
            {
                box.Text = "";
                Emit(name, "", true);
            };
            panel.Controls.AddRange(new Control[] { box, browse, clear });
            return panel;
        }

        /// <summary>
        /// Catégories personnalisées des LEDs : une ligne par catégorie (menu déroulant des catégories déjà vues,
        /// modifiable à la main), sa couleur et un bouton pour la retirer ; « Ajouter une catégorie » en dessous.
        /// </summary>
        Control ClassColorsEditor(string name, List<LedClassColor> current)
        {
            var items = current.Select(c => new LedClassColor { Class = c.Class, Color = c.Color }).ToList();
            var known = Radar.SeenClasses.All();
            const int RowHeight = 32;
            var panel = new Panel { Height = items.Count * RowHeight + 32, BackColor = Color.Transparent };

            void Commit() => Emit(name, items.Select(c => new LedClassColor { Class = c.Class, Color = c.Color }).ToList(), true);
            // Ligne ajoutée ou retirée : la page est reconstruite après l'événement (hauteur de la ligne).
            void CommitAndRebuild()
            {
                Commit();
                var tab = (string?)Tag;
                BeginInvoke(() => { if (_view != null && tab != null) Build(_view, tab, _mode); });
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int y = i * RowHeight;
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.SurfaceRaised,
                    ForeColor = Theme.Text,
                    Font = Theme.Font(9f),
                    Width = 200,
                    Location = new Point(0, y + 2),
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                };
                foreach (var k in known)
                    combo.Items.Add(k);
                combo.Text = item.Class;
                combo.SelectedIndexChanged += (_, _) => { item.Class = combo.Text; Commit(); };
                combo.Leave += (_, _) =>
                {
                    if (item.Class != combo.Text)
                    {
                        item.Class = combo.Text.Trim();
                        Commit();
                    }
                };
                var swatch = new FlatButton { Width = 44, Height = 26, Location = new Point(208, y + 1), FillOverride = Parse(item.Color) };
                swatch.Click += (_, _) =>
                {
                    using var dialog = new ColorDialog { Color = Parse(item.Color), FullOpen = true, AnyColor = true };
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                        return;
                    item.Color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    swatch.FillOverride = dialog.Color;
                    swatch.Invalidate();
                    Commit();
                };
                var remove = new FlatButton { Text = "Retirer", Width = 80, Height = 26, Location = new Point(260, y + 1) };
                remove.Click += (_, _) =>
                {
                    items.Remove(item);
                    CommitAndRebuild();
                };
                panel.Controls.AddRange(new Control[] { combo, swatch, remove });
            }

            var add = new FlatButton { Text = "+ Ajouter une catégorie", Width = 200, Height = 26, Location = new Point(0, items.Count * RowHeight + 3) };
            add.Click += (_, _) =>
            {
                // Propose d'abord une catégorie vue en jeu qui n'a pas encore de couleur.
                var next = known.FirstOrDefault(k => items.All(c => !string.Equals(c.Class, k, StringComparison.OrdinalIgnoreCase))) ?? "";
                items.Add(new LedClassColor { Class = next, Color = "#FFFFFF" });
                CommitAndRebuild();
            };
            panel.Controls.Add(add);
            return panel;
        }

        /// <summary>Liste (points de couleur, forme du cache…) : nombre d'éléments et bouton d'édition.</summary>
        Control ListEditor(PropertyInfo property, System.Collections.IList? list)
        {
            var panel = new Panel { Height = 28, BackColor = Color.Transparent };
            var count = new Label
            {
                Text = $"{list?.Count ?? 0} élément(s)",
                AutoSize = false,
                Width = 120,
                Height = 26,
                Location = new Point(0, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextMuted,
                Font = Theme.Font(9f),
            };
            var edit = new FlatButton { Text = "Modifier…", Width = 100, Height = 26, Location = new Point(124, 1) };
            edit.Click += (_, _) =>
            {
                if (_view == null)
                    return;
                var holderType = typeof(ListHolder<>).MakeGenericType(property.PropertyType.GetGenericArguments()[0]);
                var holder = Activator.CreateInstance(holderType)!;
                // Copie de travail : la liste n'est enregistrée qu'à la fermeture de la fenêtre.
                var copy = Activator.CreateInstance(property.PropertyType) as System.Collections.IList;
                foreach (var item in (System.Collections.IList?)property.GetValue(_view) ?? Array.Empty<object>())
                    copy!.Add(CloneItem(item));
                holderType.GetProperty("Items")!.SetValue(holder, copy);
                using var form = new Form
                {
                    Text = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name,
                    Size = new Size(520, 420),
                    StartPosition = FormStartPosition.CenterParent,
                    BackColor = Theme.Background,
                };
                var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = holder, ToolbarVisible = false, HelpVisible = true };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
                form.Controls.Add(grid);
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    count.Text = $"{copy!.Count} élément(s)";
                    Emit(property.Name, copy, true);
                }
            };
            panel.Controls.Add(count);
            panel.Controls.Add(edit);
            return panel;
        }

        static object CloneItem(object item)
        {
            var clone = Activator.CreateInstance(item.GetType())!;
            foreach (var p in item.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
                p.SetValue(clone, p.GetValue(item));
            return clone;
        }

        sealed class ListHolder<T>
        {
            [DisplayName("Éléments"), Description("Cliquez sur « … » pour ajouter, modifier ou supprimer des éléments.")]
            public List<T> Items { get; set; } = new();
        }
    }

    /// <summary>Curseur toujours visible + case numérique, synchronisés et modifiables tous les deux.</summary>
    public sealed class SliderBox : Panel
    {
        readonly Slider _slider;
        readonly NumericUpDown _number;
        readonly Action<double, bool> _changed;
        bool _syncing;

        public SliderBox(GaugeAttribute range, double value, bool integer, Action<double, bool> changed)
        {
            _changed = changed;
            Height = 30;
            BackColor = Color.Transparent;
            int decimals = integer || range.Step >= 1 ? 0 : range.Step >= 0.1 ? 1 : 2;
            _number = new NumericUpDown
            {
                // La case accepte aussi une valeur hors des bornes du curseur (déjà enregistrée, ou tapée).
                Minimum = (decimal)Math.Floor(Math.Min(range.Min, value)),
                Maximum = (decimal)Math.Ceiling(Math.Max(range.Max, value)),
                DecimalPlaces = decimals,
                Increment = (decimal)Math.Max(range.Step, integer ? 1 : 0.01),
                Width = 86,
                Dock = DockStyle.Right,
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Font(9.5f),
                TextAlign = HorizontalAlignment.Right,
            };
            _number.Value = Math.Clamp((decimal)Math.Round(value, decimals), _number.Minimum, _number.Maximum);
            _slider = new Slider(range, value) { Dock = DockStyle.Fill };
            var gap = new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Color.Transparent };

            _slider.Moving += v => Sync(v, final: false);
            _slider.Moved += v => Sync(v, final: true);
            _number.ValueChanged += (_, _) =>
            {
                if (_syncing)
                    return;
                double v = (double)_number.Value;
                _syncing = true;
                _slider.Value = v;
                _syncing = false;
                _changed(v, true);
            };

            Controls.Add(_slider);
            Controls.Add(gap);
            Controls.Add(_number);
        }

        void Sync(double v, bool final)
        {
            if (_syncing)
                return;
            _syncing = true;
            _number.Value = Math.Clamp(Math.Round((decimal)v, _number.DecimalPlaces), _number.Minimum, _number.Maximum);
            _syncing = false;
            _changed(v, final);
        }
    }

    /// <summary>Curseur aux couleurs AVIX : glisser, clic, flèches du clavier ; molette seulement s'il a le focus.</summary>
    public sealed class Slider : Control
    {
        readonly GaugeAttribute _range;
        double _value;
        bool _dragging;

        /// <summary>Pendant le glissement (aperçu en direct).</summary>
        public event Action<double>? Moving;
        /// <summary>Valeur définitive (souris relâchée, clavier).</summary>
        public event Action<double>? Moved;

        public Slider(GaugeAttribute range, double value)
        {
            _range = range;
            _value = Math.Clamp(value, range.Min, range.Max);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 28;
        }

        public double Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, _range.Min, _range.Max);
                Invalidate();
            }
        }

        double Snap(double v)
        {
            v = Math.Clamp(v, _range.Min, _range.Max);
            if (_range.Step > 0)
                v = _range.Min + Math.Round((v - _range.Min) / _range.Step) * _range.Step;
            return Math.Round(Math.Clamp(v, _range.Min, _range.Max), 4);
        }

        RectangleF Track => new(9, Height / 2f - 3, Math.Max(10, Width - 18), 6);

        double ValueAt(int x)
        {
            var t = Track;
            return _range.Min + Math.Clamp((x - t.X) / t.Width, 0, 1) * (_range.Max - _range.Min);
        }

        void Set(double v, bool final)
        {
            v = Snap(v);
            bool changed = v != _value;
            _value = v;
            Invalidate();
            if (changed && !final)
                Moving?.Invoke(v);
            if (final)
                Moved?.Invoke(v);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Set(ValueAt(e.X), final: false);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
                Set(ValueAt(e.X), final: false);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Set(ValueAt(e.X), final: true);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Sans le focus, la molette fait défiler la page des réglages au lieu de changer la valeur.
            if (Focused)
            {
                Set(_value + Math.Sign(e.Delta) * _range.Step, final: true);
                if (e is HandledMouseEventArgs handled)
                    handled.Handled = true;
            }
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            double big = Math.Max(_range.Step, (_range.Max - _range.Min) / 20);
            switch (e.KeyCode)
            {
                case Keys.Left or Keys.Down: Set(_value - _range.Step, true); break;
                case Keys.Right or Keys.Up: Set(_value + _range.Step, true); break;
                case Keys.PageDown: Set(_value - big, true); break;
                case Keys.PageUp: Set(_value + big, true); break;
                case Keys.Home: Set(_range.Min, true); break;
                case Keys.End: Set(_range.Max, true); break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var t = Track;
            double frac = (_value - _range.Min) / Math.Max(1e-9, _range.Max - _range.Min);
            using (var path = Theme.RoundedRect(t, 3))
            using (var brush = new SolidBrush(Theme.SurfaceRaised))
                g.FillPath(brush, path);
            var filled = new RectangleF(t.X, t.Y, (float)(t.Width * frac), t.Height);
            if (filled.Width > 1)
            {
                using var path = Theme.RoundedRect(filled, 3);
                using var brush = new SolidBrush(Theme.Accent);
                g.FillPath(brush, path);
            }
            float x = t.X + (float)(t.Width * frac);
            using var knob = new SolidBrush(Color.White);
            using var pen = new Pen(Theme.Accent, Focused ? 3 : 2);
            g.FillEllipse(knob, x - 8, t.Y + t.Height / 2 - 8, 16, 16);
            g.DrawEllipse(pen, x - 8, t.Y + t.Height / 2 - 8, 16, 16);
        }
    }

    /// <summary>Interrupteur oui/non.</summary>
    public sealed class ToggleSwitch : Control
    {
        bool _checked;

        public event Action? CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(46, 24);
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;
                _checked = value;
                Invalidate();
            }
        }

        void Toggle()
        {
            _checked = !_checked;
            Invalidate();
            CheckedChanged?.Invoke();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
                Toggle();
            base.OnMouseClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
                Toggle();
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var track = new RectangleF(1, (Height - 22) / 2f, 44, 22);
            using (var path = Theme.RoundedRect(track, 11))
            using (var brush = new SolidBrush(_checked ? Theme.Accent : Theme.SurfaceRaised))
            using (var pen = new Pen(_checked ? Theme.Accent : Theme.Border, Focused ? 2 : 1))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            float x = _checked ? track.Right - 19 : track.X + 3;
            using var knob = new SolidBrush(_checked ? Color.Black : Theme.TextMuted);
            g.FillEllipse(knob, x, track.Y + 3, 16, 16);
        }
    }
}
