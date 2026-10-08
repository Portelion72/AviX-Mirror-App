using AviXMirror.Ui;

namespace AviXMirror;

/// <summary>Fenêtre principale aux couleurs AVIX_3D : choix du mode, démarrage, aperçu du VoCore et réglages.</summary>
public sealed class MainForm : Form
{
    readonly MirrorEngine _engine = new();
    readonly SettingsPanel _panel = new() { Dock = DockStyle.Fill };
    // Réglages affichés (vue du profil choisi : communs + valeurs propres au jeu).
    Settings _view;
    readonly TabStrip _tabs = new() { Dock = DockStyle.Fill };
    readonly HeaderBar _header = new();
    readonly FlatButton _startStop = new() { Text = "Démarrer", Primary = true, Height = 52, Dock = DockStyle.Fill };
    readonly FlatButton _calibrate = new() { Text = "Calibrer la zone", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    readonly FlatButton _alignHud = new() { Text = "Aligner les flèches", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
    readonly TableLayoutPanel _captureActions = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
    readonly FlatButton _installAc = new() { Text = "Installer l'app Assetto Corsa", Dock = DockStyle.Fill };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly Dictionary<MirrorMode, ModeTile> _tiles = new();
    readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    readonly MirrorPreview _preview;
    readonly FlatButton _updateBanner = new() { Primary = true, Dock = DockStyle.Top, Height = 40, Visible = false };
    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 6 * 3600 * 1000 };
    Util.UpdateInfo? _update;

    // Réglages communs + profils de chaque jeu (enregistrés tels quels).
    Settings _settings;
    // Profil affiché dans les onglets : null = tous les jeux (réglages communs).
    RadarGame? _profile;
    bool _profileChosenByUser;
    readonly ComboBox _profileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = 300 };
    readonly Label _profileInfo = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly FlatButton _profileReset = new() { Text = "Reprendre les réglages communs", Width = 230, Dock = DockStyle.Right };
    readonly FlatButton _gameGuide = new() { Text = "Guide du jeu", Width = 130, Dock = DockStyle.Right, Margin = new Padding(8, 0, 0, 0) };

    public MainForm()
    {
        _settings = Settings.Load();
        // « Lancer avec Windows » suit l'état réel de Windows (et l'emplacement actuel de l'exe).
        Util.WindowsIntegration.RefreshStartWithWindows();
        _settings.StartWithWindows = Util.WindowsIntegration.StartsWithWindows;
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

        _view = _settings.ForGame(null);
        _tabs.SetTabs(Tabs.For(_view.Mode));
        _tabs.SelectedChanged += ShowTab;
        ShowTab(Tabs.General);
        // Curseurs : la valeur s'applique en direct pendant qu'on fait glisser.
        _panel.ValueChanged += OnPropertyChanged;
        SetupProfiles();

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
        _alignHud.Click += (_, _) => AlignHud();
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        SelectMode(_settings.Mode, restart: false);
        RefreshStatus();

        Shown += async (_, _) =>
        {
            // Premier lancement (ou tutoriel demandé) : le tutoriel d'abord, fenêtre ouverte.
            if (_settings.ShowTutorial || Settings.FirstRun)
            {
                ShowTutorial();
                if (_settings.AutoStart && !_engine.Running)
                    ToggleRunning();
            }
            else if (_settings.AutoStart)
            {
                ToggleRunning();
                WindowState = FormWindowState.Minimized;
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
        using var form = new TutorialForm(_view.Mode, _settings.ShowTutorial,
            mode => SelectMode(mode, restart: true), InstallAcApp,
            Util.WindowsIntegration.HasDesktopShortcut, Util.WindowsIntegration.StartsWithWindows);
        form.ShowDialog(this);
        // Dernière étape : raccourci sur le bureau et lancement avec Windows.
        if (form.CreateDesktopShortcut && !Util.WindowsIntegration.CreateDesktopShortcut(Theme.CreateAppIcon()))
            MessageBox.Show(this, "Le raccourci n'a pas pu être créé sur le bureau.", "AviX Mirror", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (form.StartWithWindows is { } startup && startup != Util.WindowsIntegration.StartsWithWindows)
        {
            Util.WindowsIntegration.SetStartWithWindows(startup);
            _settings.StartWithWindows = Util.WindowsIntegration.StartsWithWindows;
            var stored = Settings.Load();
            stored.StartWithWindows = _settings.StartWithWindows;
            stored.Save();
            RefreshView();
        }
        // « Afficher au démarrage » : enregistré tout de suite, sans toucher aux autres réglages en cours.
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
        _captureActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _captureActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _captureActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _captureActions.BackColor = Theme.Background;
        _captureActions.Controls.Add(_calibrate, 0, 0);
        _captureActions.Controls.Add(_alignHud, 1, 0);
        modeAction.Controls.Add(_captureActions);
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
        var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Background };
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        column.Controls.Add(new SectionLabel("Aperçu VoCore") { Dock = DockStyle.Fill, Margin = new Padding(0) });
        var previewCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(18) };
        _preview.BackColor = Theme.Surface;
        previewCard.Controls.Add(_preview);
        column.Controls.Add(previewCard);

        // Profil : réglages communs ou propres à un jeu.
        var profileRow = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0), BackColor = Theme.Background };
        var profileLabel = new Label
        {
            Text = "PROFIL",
            Dock = DockStyle.Left,
            Width = 64,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Accent,
            Font = Theme.Font(9f, FontStyle.Bold),
        };
        _profileBox.Dock = DockStyle.Left;
        _profileBox.BackColor = Theme.SurfaceRaised;
        _profileBox.ForeColor = Theme.Text;
        _profileBox.Font = Theme.Font(10f);
        _profileInfo.ForeColor = Theme.TextMuted;
        _profileInfo.Font = Theme.Font(8.5f);
        _profileInfo.Padding = new Padding(12, 0, 8, 0);
        profileRow.Controls.Add(_profileInfo);
        profileRow.Controls.Add(_profileReset);
        profileRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Theme.Background });
        profileRow.Controls.Add(_gameGuide);
        profileRow.Controls.Add(_profileBox);
        profileRow.Controls.Add(profileLabel);
        column.Controls.Add(profileRow);

        _tabs.Margin = new Padding(0, 6, 0, 0);
        column.Controls.Add(_tabs);
        var gridCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(2) };
        gridCard.Controls.Add(_panel);
        column.Controls.Add(gridCard);
        return column;
    }

    Settings EditedSettings() => _settings;

    // ---------- Profils par jeu ----------

    void SetupProfiles()
    {
        _profileBox.Items.Add("Tous les jeux (réglages communs)");
        foreach (var game in Radar.Games.All)
            _profileBox.Items.Add(game.Name + (game.Beta ? " (beta)" : ""));
        _profileBox.SelectedIndex = 0;
        _profileBox.SelectedIndexChanged += (_, _) =>
        {
            _profileChosenByUser = true;
            SelectProfile(_profileBox.SelectedIndex == 0 ? null : Radar.Games.All[_profileBox.SelectedIndex - 1].Game);
        };
        _gameGuide.Click += (_, _) => ShowGameGuide();
        _profileReset.Click += (_, _) =>
        {
            if (_profile is not { } game)
                return;
            if (MessageBox.Show(this, $"{Radar.Games.Get(game).Name} reprendra tous les réglages communs. Continuer ?",
                    "Profil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _settings.ClearOverrides(game);
            RefreshView();
            _engine.UpdateLive(_settings);
        };
        RefreshView();
    }

    /// <summary>Guide de configuration par jeu (jeu du profil affiché, ou jeu détecté).</summary>
    void ShowGameGuide()
    {
        using var form = new GameGuideForm(_profile ?? _engine.CurrentGame, game =>
        {
            foreach (var (name, value, _) in GameGuides.Get(game).Recommended)
                _settings.Set(game, name, value);
            _settings.Save();
            SelectProfile(game);
            _profileChosenByUser = true;
            if (_engine.Running)
                ApplySettings(); // le mode peut changer : redémarrage
        });
        form.ShowDialog(this);
    }

    /// <summary>Affiche les réglages d'un profil (null = réglages communs).</summary>
    void SelectProfile(RadarGame? game)
    {
        _profile = game;
        int index = game is { } g ? Array.FindIndex(Radar.Games.All, x => x.Game == g) + 1 : 0;
        if (_profileBox.SelectedIndex != index)
            _profileBox.SelectedIndex = index;
        RefreshView();
    }

    /// <summary>Recharge la grille avec les réglages effectifs du profil affiché.</summary>
    void RefreshView()
    {
        var view = _view = _settings.ForGame(_profile);
        UpdateTabs(view.Mode);
        _panel.Build(view, _tabs.Selected, view.Mode);
        _profileReset.Visible = _profile != null;
        _profileInfo.Text = _profile is { } game
            ? ProfileText(game)
            : "Réglages utilisés par tous les jeux. Choisissez un jeu pour lui donner ses propres réglages.";
        foreach (var (m, tile) in _tiles)
            tile.Selected = m == view.Mode;
        _captureActions.Visible = view.Mode == MirrorMode.Capture;
        _installAc.Visible = view.Mode != MirrorMode.Capture;
    }

    /// <summary>Le profil du jeu détecté s'affiche tout seul, tant que l'utilisateur n'en a pas choisi un.</summary>
    void FollowDetectedGame()
    {
        if (_profileChosenByUser || !_engine.Running || _engine.CurrentGame == _profile)
            return;
        SelectProfile(_engine.CurrentGame);
        _profileChosenByUser = false;
    }

    /// <summary>Onglets du mode : celui du mode (Capture, Radar ou Caméra AC) et les onglets communs.</summary>
    void UpdateTabs(MirrorMode mode)
    {
        var tabs = Tabs.For(mode);
        var selected = _tabs.Selected;
        _tabs.SetTabs(tabs);
        // L'onglet d'un autre mode disparaît : on passe à celui du mode.
        if (!tabs.Contains(selected))
            SelectTabSilently(tabs[1]);
        else
            SelectTabSilently(selected);
    }

    bool _switchingTabs;

    /// <summary>Change l'onglet sans reconstruire les réglages (l'appelant s'en charge).</summary>
    void SelectTabSilently(string tab)
    {
        _switchingTabs = true;
        try { _tabs.Selected = tab; }
        finally { _switchingTabs = false; }
    }

    /// <summary>Affiche seulement les réglages de l'onglet choisi.</summary>
    void ShowTab(string tab)
    {
        if (!_switchingTabs)
            _panel.Build(_view, tab, _view.Mode);
    }

    /// <summary>
    /// Réglage modifié dans le panneau : enregistré dans le profil affiché et appliqué en direct.
    /// <paramref name="final"/> est faux pendant qu'on fait glisser un curseur (aperçu).
    /// </summary>
    void OnPropertyChanged(string name, object? value, bool final)
    {
        _settings.Set(_profile, name, value);
        if (!final)
        {
            _engine.UpdateLive(_settings);
            return;
        }
        if (_profile != null)
            _profileInfo.Text = ProfileText(_profile.Value);
        if (name == nameof(Settings.StartWithWindows) && value is bool startup)
        {
            if (!Util.WindowsIntegration.SetStartWithWindows(startup))
                MessageBox.Show(this, "Impossible de modifier le lancement avec Windows.", "AviX Mirror", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _settings.StartWithWindows = Util.WindowsIntegration.StartsWithWindows;
        }
        if (name is nameof(Settings.AccentColor) or nameof(Settings.UiFont))
        {
            // Nouvelle charte : on redessine toute l'interface.
            Theme.Configure(_settings.AccentColor, _settings.UiFont);
            Icon = Theme.CreateAppIcon();
            _panel.ApplyTheme();
            BeginInvoke(() => _panel.Build(_view, _tabs.Selected, _view.Mode));
            Refresh();
        }
        if (name == nameof(Settings.Mode))
            BeginInvoke(() => SelectMode(_view.Mode, restart: true)); // reconstruit les onglets après l'événement
        else
            _engine.UpdateLive(_settings); // luminosité, champ de vision… en direct
    }

    string ProfileText(RadarGame game) =>
        $"{_settings.OverrideCount(game)} réglage(s) propre(s) à ce jeu. Capture, Radar, Caméra AC, ATH, mode et alertes " +
        "s'enregistrent pour ce jeu ; les autres onglets sont communs.";

    void SelectMode(MirrorMode mode, bool restart)
    {
        foreach (var (m, tile) in _tiles)
            tile.Selected = m == mode;
        // Seuls les boutons utiles au mode choisi sont affichés.
        _captureActions.Visible = mode == MirrorMode.Capture;
        _installAc.Visible = mode != MirrorMode.Capture;

        if (_view.Mode != mode)
            _settings.Set(_profile, nameof(Settings.Mode), mode);
        // Onglets du mode, et ouverture de l'onglet de réglages du mode choisi.
        var tabs = Tabs.For(mode);
        _tabs.SetTabs(tabs);
        SelectTabSilently(tabs[1]);
        RefreshView();
        if (restart && _engine.Running)
            ApplySettings();
    }

    void RefreshStatus()
    {
        FollowDetectedGame();
        _status.Text = _engine.Running ? _engine.Status : "Choisissez un mode puis cliquez sur DÉMARRER.";
        _header.SetState(_engine.Running, _engine.Running ? "EN COURS" : "ARRÊTÉ");
        _startStop.Text = _engine.Running ? "Arrêter" : "Démarrer";
        _startStop.FillOverride = _engine.Running ? Theme.Danger : null;
        _startStop.Invalidate();
    }

    void ApplySettings()
    {
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
            _settings.Save();
            _engine.Start(_settings);
        }
        RefreshStatus();
        _preview.Invalidate();
    }

    void Calibrate()
    {
        var capture = _engine.Capture;
        var effective = _engine.Effective;
        if (!_engine.Running || effective.Mode != MirrorMode.Capture || capture == null)
        {
            MessageBox.Show(this,
                "Démarrez d'abord le rétroviseur en mode Capture, avec le jeu lancé.",
                "Calibrer la zone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var current = new Rectangle(effective.CropX, effective.CropY, effective.CropWidth, effective.CropHeight);
        var margins = new Padding(Math.Max(0, effective.MaskMarginLeft), Math.Max(0, effective.MaskMarginTop),
            Math.Max(0, effective.MaskMarginRight), Math.Max(0, effective.MaskMarginBottom));
        DialogResult result;
        List<MaskSample> samples;
        List<MaskPoint> shape;
        bool shapeSmooth;
        Rectangle sel;
        _engine.Calibrating = true;
        try
        {
            using var form = new CalibrationForm(capture, _engine.Frames, current, margins, effective.MaskSamples,
                effective.MaskShape, effective.MaskShapeSmooth);
            result = form.ShowDialog(this);
            sel = form.Selection;
            samples = form.Samples;
            shape = form.Shape;
            shapeSmooth = form.ShapeSmooth;
        }
        finally
        {
            _engine.Calibrating = false;
        }
        if (result == DialogResult.OK)
        {
            // La zone s'enregistre dans le profil du jeu capturé.
            var game = _engine.CurrentGame;
            _settings.Set(game, nameof(Settings.CropX), sel.X);
            _settings.Set(game, nameof(Settings.CropY), sel.Y);
            _settings.Set(game, nameof(Settings.CropWidth), sel.Width);
            _settings.Set(game, nameof(Settings.CropHeight), sel.Height);
            _settings.Set(game, nameof(Settings.MaskSamples), samples);
            _settings.Set(game, nameof(Settings.MaskShape), shape);
            _settings.Set(game, nameof(Settings.MaskShapeSmooth), shapeSmooth);
            _settings.Save();
            RefreshView();
        }
        // Applique la nouvelle zone (ou restaure l'ancienne si annulé).
        _engine.UpdateLive(_settings);
    }

    /// <summary>
    /// Mode Capture : place les flèches de l'ATH sur les voitures en les faisant glisser dans l'image du rétro.
    /// Horizon, centre et champ de vision s'enregistrent dans le profil du jeu capturé.
    /// </summary>
    void AlignHud()
    {
        var effective = _engine.Effective;
        if (!_engine.Running || effective.Mode != MirrorMode.Capture || _engine.Capture == null)
        {
            MessageBox.Show(this,
                "Démarrez d'abord le rétroviseur en mode Capture, avec le jeu lancé.",
                "Aligner les flèches", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult result;
        double horizon, center, fov;
        _engine.Calibrating = true;
        try
        {
            using var form = new HudAlignForm(_engine, effective);
            result = form.ShowDialog(this);
            (horizon, center, fov) = (form.Horizon, form.Center, form.Fov);
        }
        finally
        {
            _engine.Calibrating = false;
        }
        if (result != DialogResult.OK)
            return;
        var game = _engine.CurrentGame;
        _settings.Set(game, nameof(Settings.HudCaptureHorizon), horizon);
        _settings.Set(game, nameof(Settings.HudCaptureCenter), center);
        _settings.Set(game, nameof(Settings.HudCaptureFov), fov);
        _settings.Save();
        RefreshView();
        _engine.UpdateLive(_settings);
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
