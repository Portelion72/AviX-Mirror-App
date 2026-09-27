using AviXMirror.Ui;

namespace AviXMirror;

/// <summary>Fenêtre principale aux couleurs AVIX_3D : choix du mode, démarrage, aperçu du VoCore et réglages.</summary>
public sealed class MainForm : Form
{
    readonly MirrorEngine _engine = new();
    readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
    readonly HeaderBar _header = new();
    readonly FlatButton _startStop = new() { Text = "Démarrer", Primary = true, Height = 52, Dock = DockStyle.Fill };
    readonly FlatButton _calibrate = new() { Text = "Calibrer la zone du rétro", Dock = DockStyle.Fill };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly Dictionary<MirrorMode, ModeTile> _tiles = new();
    readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    readonly MirrorPreview _preview;
    Settings _settings;

    public MainForm()
    {
        _settings = Settings.Load();
        Theme.Configure(_settings.AccentColor, _settings.UiFont);

        Text = "AVIX_3D Mirror";
        Icon = Theme.CreateAppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 780);
        MinimumSize = new Size(980, 660);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        DoubleBuffered = true;

        _preview = new MirrorPreview(_engine.Frames, () => _engine.LedSides) { Dock = DockStyle.Fill };

        _grid.SelectedObject = _settings.Clone();
        StyleGrid();
        _grid.PropertyValueChanged += (_, e) => OnPropertyChanged(e);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20, 16, 20, 8),
            BackColor = Theme.Background,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildLeftColumn(), 0, 0);
        body.Controls.Add(BuildRightColumn(), 1, 0);

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Text = $"avix3d.com   ·   AVIX_3D Mirror {Application.ProductVersion.Split('+')[0]}   ·   Le Mans Ultimate · Assetto Corsa",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Font = Theme.Font(8f),
        };

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(_header);

        _startStop.Click += (_, _) => ToggleRunning();
        _calibrate.Click += (_, _) => Calibrate();
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        SelectMode(_settings.Mode, restart: false);
        RefreshStatus();

        Shown += (_, _) =>
        {
            if (_settings.AutoStart)
            {
                ToggleRunning();
                WindowState = FormWindowState.Minimized;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    Control BuildLeftColumn()
    {
        var column = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Background,
            Padding = new Padding(0, 0, 16, 0),
            AutoScroll = true,
        };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Add(Control c, int height, Padding? margin = null)
        {
            c.Dock = DockStyle.Fill;
            c.Margin = margin ?? new Padding(0, 0, 0, 8);
            column.RowStyles.Add(new RowStyle(SizeType.Absolute, height + c.Margin.Vertical));
            column.Controls.Add(c);
        }

        Add(new SectionLabel("Mode"), 26, new Padding(0));
        AddTile(MirrorMode.Radar, "Radar", "Vue synthétique des voitures derrière vous, sur le tracé du circuit. LMU et Assetto Corsa.");
        AddTile(MirrorMode.CameraAssettoCorsa, "Caméra Assetto Corsa", "Vraie vue arrière rendue hors écran par l'app CSP.");
        AddTile(MirrorMode.Capture, "Capture LMU", "Recopie le rétro virtuel du jeu (expérimental).");

        Add(new SectionLabel("Contrôle"), 26, new Padding(0, 8, 0, 0));
        Add(_startStop, 52, new Padding(0, 0, 0, 10));

        var save = new FlatButton { Text = "Enregistrer les réglages" };
        save.Click += (_, _) => ApplySettings();
        Add(save, 38);
        var installAc = new FlatButton { Text = "Installer l'app Assetto Corsa" };
        installAc.Click += (_, _) => InstallAcApp();
        Add(installAc, 38);
        Add(_calibrate, 38);

        Add(new SectionLabel("État"), 26, new Padding(0, 8, 0, 0));
        var statusCard = new Card { Padding = new Padding(14, 12, 14, 12) };
        _status.ForeColor = Theme.Text;
        _status.BackColor = Theme.Surface;
        _status.Font = Theme.Font(9f);
        statusCard.Controls.Add(_status);
        statusCard.Dock = DockStyle.Fill;
        statusCard.Margin = new Padding(0);
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        column.Controls.Add(statusCard);

        return column;

        void AddTile(MirrorMode mode, string title, string description)
        {
            var tile = new ModeTile { Title = title, Description = description };
            tile.Click += (_, _) => SelectMode(mode, restart: true);
            _tiles[mode] = tile;
            Add(tile, 66);
        }
    }

    Control BuildRightColumn()
    {
        var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background };
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        column.Controls.Add(new SectionLabel("Aperçu VoCore") { Dock = DockStyle.Fill, Margin = new Padding(0) });
        var previewCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(18) };
        _preview.BackColor = Theme.Surface;
        previewCard.Controls.Add(_preview);
        column.Controls.Add(previewCard);

        column.Controls.Add(new SectionLabel("Réglages") { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) });
        var gridCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(2) };
        gridCard.Controls.Add(_grid);
        column.Controls.Add(gridCard);
        return column;
    }

    void StyleGrid()
    {
        _grid.Font = Theme.Font(9f);
        _grid.BackColor = Theme.Surface;
        _grid.ViewBackColor = Theme.Surface;
        _grid.ViewForeColor = Theme.Text;
        _grid.ViewBorderColor = Theme.Surface;
        _grid.LineColor = Theme.SurfaceRaised;
        _grid.CategoryForeColor = Theme.Accent;
        _grid.CategorySplitterColor = Theme.SurfaceRaised;
        _grid.HelpBackColor = Theme.SurfaceRaised;
        _grid.HelpForeColor = Theme.TextMuted;
        _grid.HelpBorderColor = Theme.SurfaceRaised;
        _grid.SelectedItemWithFocusBackColor = Theme.AccentDark;
        _grid.SelectedItemWithFocusForeColor = Color.White;
        _grid.CommandsBackColor = Theme.Surface;
        _grid.CommandsForeColor = Theme.Text;
        _grid.DisabledItemForeColor = Theme.TextMuted;
    }

    Settings EditedSettings() => (Settings)_grid.SelectedObject;

    void OnPropertyChanged(PropertyValueChangedEventArgs e)
    {
        var edited = EditedSettings();
        var name = e.ChangedItem?.PropertyDescriptor?.Name;
        if (name is nameof(Settings.AccentColor) or nameof(Settings.UiFont))
        {
            // Nouvelle charte : on redessine toute l'interface.
            Theme.Configure(edited.AccentColor, edited.UiFont);
            Icon = Theme.CreateAppIcon();
            StyleGrid();
            Refresh();
        }
        if (name == nameof(Settings.Mode))
            SelectMode(edited.Mode, restart: true);
        else
            _engine.UpdateLive(edited); // luminosité, champ de vision… en direct
    }

    void SelectMode(MirrorMode mode, bool restart)
    {
        foreach (var (m, tile) in _tiles)
            tile.Selected = m == mode;
        _calibrate.Visible = mode == MirrorMode.Capture;

        var edited = EditedSettings();
        if (edited.Mode != mode)
        {
            edited.Mode = mode;
            _grid.Refresh();
        }
        if (restart && _engine.Running)
            ApplySettings();
    }

    void RefreshStatus()
    {
        _status.Text = _engine.Running ? _engine.Status : "Choisissez un mode puis cliquez sur DÉMARRER.";
        _header.SetState(_engine.Running, _engine.Running ? "EN COURS" : "ARRÊTÉ");
        _startStop.Text = _engine.Running ? "Arrêter" : "Démarrer";
        _startStop.FillOverride = _engine.Running ? Theme.Danger : null;
        _startStop.Invalidate();
    }

    void ApplySettings()
    {
        _settings = EditedSettings().Clone();
        _settings.Save();
        if (_engine.Running)
            _engine.Start(_settings);
        RefreshStatus();
    }

    void ToggleRunning()
    {
        if (_engine.Running)
        {
            _engine.Stop();
        }
        else
        {
            _settings = EditedSettings().Clone();
            _settings.Save();
            _engine.Start(_settings);
        }
        RefreshStatus();
        _preview.Invalidate();
    }

    void Calibrate()
    {
        var capture = _engine.Capture;
        if (!_engine.Running || _settings.Mode != MirrorMode.Capture || capture == null)
        {
            MessageBox.Show(this,
                "Démarrez d'abord le rétroviseur en mode Capture, avec Le Mans Ultimate lancé.",
                "Calibrer la zone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var current = new Rectangle(_settings.CropX, _settings.CropY, _settings.CropWidth, _settings.CropHeight);
        using var form = new CalibrationForm(capture, _engine.Frames, current);
        var result = form.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            var sel = form.Selection;
            var edited = EditedSettings();
            edited.CropX = sel.X;
            edited.CropY = sel.Y;
            edited.CropWidth = sel.Width;
            edited.CropHeight = sel.Height;
            _grid.Refresh();
        }
        // Applique la nouvelle zone (ou restaure l'ancienne si annulé).
        _settings.CropX = EditedSettings().CropX;
        _settings.CropY = EditedSettings().CropY;
        _settings.CropWidth = EditedSettings().CropWidth;
        _settings.CropHeight = EditedSettings().CropHeight;
        _settings.Save();
        capture.SetCrop(new Rectangle(_settings.CropX, _settings.CropY, _settings.CropWidth, _settings.CropHeight));
    }

    void InstallAcApp()
    {
        var folder = Util.AcInstaller.FindAcFolder();
        if (folder == null)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Dossier d'Assetto Corsa (celui qui contient AssettoCorsa.exe)",
                UseDescriptionForTitle = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            folder = dialog.SelectedPath;
            if (!File.Exists(Path.Combine(folder, "AssettoCorsa.exe")))
            {
                MessageBox.Show(this, "AssettoCorsa.exe est introuvable dans ce dossier.", "Assetto Corsa",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        try
        {
            var target = Util.AcInstaller.Install(folder);
            MessageBox.Show(this,
                "App installée dans :\n" + target + "\n\n" +
                "Elle nécessite Custom Shaders Patch (Content Manager > Paramètres > Custom Shaders Patch). " +
                "Elle démarre toute seule avec Assetto Corsa ; rien à ouvrir en jeu.\n\n" +
                "Dans AviX Mirror, choisissez Mode = Radar.",
                "Assetto Corsa", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Installation impossible : " + ex.Message, "Assetto Corsa",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statusTimer.Stop();
        _engine.Dispose();
        base.OnFormClosing(e);
    }
}
