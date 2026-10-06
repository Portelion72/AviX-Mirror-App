using AviXMirror.Ui;

namespace AviXMirror;

/// <summary>Fenêtre principale aux couleurs AVIX_3D : choix du mode, démarrage, aperçu du VoCore et réglages.</summary>
public sealed class MainForm : Form
{
    readonly MirrorEngine _engine = new();
    readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, PropertySort = PropertySort.NoSort, ToolbarVisible = false };
    readonly TabStrip _tabs = new() { Dock = DockStyle.Fill };
    readonly HeaderBar _header = new();
    readonly FlatButton _startStop = new() { Text = "Démarrer", Primary = true, Height = 52, Dock = DockStyle.Fill };
    readonly FlatButton _calibrate = new() { Text = "Calibrer la zone du rétro", Dock = DockStyle.Fill };
    readonly FlatButton _installAc = new() { Text = "Installer l'app Assetto Corsa", Dock = DockStyle.Fill };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly Dictionary<MirrorMode, ModeTile> _tiles = new();
    readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    readonly MirrorPreview _preview;
    readonly FlatButton _updateBanner = new() { Primary = true, Dock = DockStyle.Top, Height = 40, Visible = false };
    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 6 * 3600 * 1000 };
    Util.UpdateInfo? _update;
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
        _tabs.SetTabs(Tabs.All);
        _tabs.SelectedChanged += ShowTab;
        ShowTab(Tabs.General);
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
            Text = $"avix3d.com   ·   AVIX_3D Mirror {Util.UpdateChecker.CurrentVersion.ToString(3)}   ·   © {DateTime.Now.Year} AVIX_3D — tous droits réservés",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Font = Theme.Font(8f),
        };

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(_updateBanner);
        Controls.Add(_header);

        // Nouvelle version : bandeau en haut de la fenêtre, vérifié au démarrage puis toutes les 6 h.
        _updateBanner.Click += (_, _) => OpenUpdatePage();
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();

        _startStop.Click += (_, _) => ToggleRunning();
        _calibrate.Click += (_, _) => Calibrate();
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        SelectMode(_settings.Mode, restart: false);
        RefreshStatus();

        Shown += async (_, _) =>
        {
            if (_settings.AutoStart)
            {
                ToggleRunning();
                WindowState = FormWindowState.Minimized;
            }
            else if (_settings.ShowTutorial)
            {
                ShowTutorial();
            }
            _updateTimer.Start();
            await CheckForUpdatesAsync();
        };

        // Extinction ou mise en veille du PC : l'écran VoCore et les LEDs sont éteints.
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    bool _resumeAfterSleep;

    /// <summary>Tutoriel de prise en main (premier démarrage, ou bouton « Tutoriel »).</summary>
    void ShowTutorial()
    {
        using var form = new TutorialForm(EditedSettings().Mode, _settings.ShowTutorial,
            mode => SelectMode(mode, restart: true), InstallAcApp);
        form.ShowDialog(this);
        // « Afficher au démarrage » : enregistré tout de suite, sans toucher aux autres réglages en cours.
        EditedSettings().ShowTutorial = form.ShowAtStartup;
        _settings.ShowTutorial = form.ShowAtStartup;
        var saved = Settings.Load();
        saved.ShowTutorial = form.ShowAtStartup;
        saved.Save();
    }

    async Task CheckForUpdatesAsync()
    {
        if (!EditedSettings().CheckUpdates)
            return;
        var update = await Util.UpdateChecker.CheckAsync();
        if (update == null || IsDisposed)
            return;
        bool isNew = _update?.Version != update.Version;
        _update = update;
        _updateBanner.Text = $"Nouvelle version {update.Tag} disponible (vous avez la {Util.UpdateChecker.CurrentVersion.ToString(3)}) — cliquez pour la télécharger";
        _updateBanner.Visible = true;
        if (isNew && WindowState == FormWindowState.Minimized)
            FlashTaskbar();
    }

    void OpenUpdatePage()
    {
        var url = _update?.Url ?? Util.UpdateChecker.ReleasesPage;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible d'ouvrir le navigateur : " + ex.Message + "\n\n" + url, "Mise à jour",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags, Count, Timeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Fait clignoter l'icône de la barre des tâches (application réduite).</summary>
    void FlashTaskbar()
    {
        var info = new FlashInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FlashInfo>(),
            Hwnd = Handle,
            Flags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG : jusqu'à ce que la fenêtre soit ouverte
        };
        FlashWindowEx(ref info);
    }

    void OnSessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        if (InvokeRequired)
            Invoke(() => _engine.Stop());
        else
            _engine.Stop();
    }

    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        void Apply()
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend && _engine.Running)
            {
                _resumeAfterSleep = true;
                _engine.Stop();
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume && _resumeAfterSleep)
            {
                _resumeAfterSleep = false;
                _engine.Start(_settings);
            }
            RefreshStatus();
        }
        if (InvokeRequired)
            Invoke(Apply);
        else
            Apply();
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
        AddTile(MirrorMode.Radar, "Radar", "Vue synthétique des voitures derrière vous. LMU, AC ; beta : ACC, AC EVO, AMS2, iRacing, F1, rF2.");
        AddTile(MirrorMode.CameraAssettoCorsa, "Caméra Assetto Corsa", "Vraie vue arrière rendue hors écran par l'app CSP.");
        AddTile(MirrorMode.Capture, "Capture du rétro", "Recopie le rétro virtuel du jeu (LMU ; autres jeux en beta) et le cache à l'écran.");

        Add(new SectionLabel("Contrôle"), 26, new Padding(0, 8, 0, 0));
        Add(_startStop, 52, new Padding(0, 0, 0, 10));

        var save = new FlatButton { Text = "Enregistrer les réglages" };
        save.Click += (_, _) => ApplySettings();
        Add(save, 38);
        var tutorial = new FlatButton { Text = "Tutoriel de prise en main" };
        tutorial.Click += (_, _) => ShowTutorial();
        Add(tutorial, 38);
        _installAc.Click += (_, _) => InstallAcApp();
        // Même emplacement pour les deux boutons : un seul est visible selon le mode.
        var modeAction = new Panel { BackColor = Theme.Background };
        modeAction.Controls.Add(_installAc);
        modeAction.Controls.Add(_calibrate);
        Add(modeAction, 38);

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
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        column.Controls.Add(new SectionLabel("Aperçu VoCore") { Dock = DockStyle.Fill, Margin = new Padding(0) });
        var previewCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(18) };
        _preview.BackColor = Theme.Surface;
        previewCard.Controls.Add(_preview);
        column.Controls.Add(previewCard);

        _tabs.Margin = new Padding(0, 10, 0, 0);
        column.Controls.Add(_tabs);
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

    /// <summary>Affiche seulement les réglages de l'onglet choisi.</summary>
    void ShowTab(string tab)
    {
        _grid.BrowsableAttributes = new System.ComponentModel.AttributeCollection(
            new System.ComponentModel.CategoryAttribute(tab), System.ComponentModel.BrowsableAttribute.Yes);
        _grid.Refresh();
    }

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
        // Ouvre l'onglet de réglages du mode choisi.
        _tabs.Selected = mode switch
        {
            MirrorMode.Capture => Tabs.Capture,
            MirrorMode.Radar => Tabs.Radar,
            _ => Tabs.Camera,
        };
        // Seuls les boutons utiles au mode choisi sont affichés.
        _calibrate.Visible = mode == MirrorMode.Capture;
        _installAc.Visible = mode != MirrorMode.Capture;

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
        var margins = new Padding(Math.Max(0, _settings.MaskMarginLeft), Math.Max(0, _settings.MaskMarginTop),
            Math.Max(0, _settings.MaskMarginRight), Math.Max(0, _settings.MaskMarginBottom));
        DialogResult result;
        List<MaskSample> samples;
        Rectangle sel;
        _engine.Calibrating = true;
        try
        {
            using var form = new CalibrationForm(capture, _engine.Frames, current, margins, _settings.MaskSamples);
            result = form.ShowDialog(this);
            sel = form.Selection;
            samples = form.Samples;
        }
        finally
        {
            _engine.Calibrating = false;
        }
        if (result == DialogResult.OK)
        {
            var edited = EditedSettings();
            edited.CropX = sel.X;
            edited.CropY = sel.Y;
            edited.CropWidth = sel.Width;
            edited.CropHeight = sel.Height;
            edited.MaskSamples = samples;
            _settings.MaskSamples = samples.Select(x => new MaskSample { Side = x.Side, Position = x.Position, Distance = x.Distance }).ToList();
            _grid.Refresh();
            _engine.UpdateLive(edited);
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
                "Dans AviX Mirror, choisissez le mode Radar ou Caméra Assetto Corsa.",
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
        _updateTimer.Stop();
        Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _engine.Dispose();
        base.OnFormClosing(e);
    }
}
