namespace AviXMirror;

/// <summary>Fenêtre de réglages et de contrôle.</summary>
public sealed class MainForm : Form
{
    readonly MirrorEngine _engine = new();
    readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
    readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(6) };
    readonly Button _startStop = new() { Text = "Démarrer", AutoSize = true };
    readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    Settings _settings;

    public MainForm()
    {
        _settings = Settings.Load();

        Text = "AviX Mirror — rétroviseur VoCore pour Le Mans Ultimate";
        Width = 620;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        _grid.SelectedObject = _settings.Clone();
        // Les réglages s'appliquent en direct (luminosité, champ de vision…) ; « Appliquer » les enregistre.
        _grid.PropertyValueChanged += (_, _) => _engine.UpdateLive(EditedSettings());

        var apply = new Button { Text = "Appliquer", AutoSize = true };
        var calibrate = new Button { Text = "Calibrer la zone", AutoSize = true };
        var installAc = new Button { Text = "Installer l'app Assetto Corsa", AutoSize = true };

        _startStop.Click += (_, _) => ToggleRunning();
        apply.Click += (_, _) => ApplySettings();
        calibrate.Click += (_, _) => Calibrate();
        installAc.Click += (_, _) => InstallAcApp();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), WrapContents = true };
        buttons.Controls.AddRange(new Control[] { _startStop, apply, calibrate, installAc });

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 150, ColumnCount = 1, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.Controls.Add(buttons, 0, 0);
        bottom.Controls.Add(_status, 0, 1);

        Controls.Add(_grid);
        Controls.Add(bottom);

        _statusTimer.Tick += (_, _) => _status.Text = _engine.Status;
        _statusTimer.Start();

        Shown += (_, _) =>
        {
            if (_settings.AutoStart)
            {
                ToggleRunning();
                WindowState = FormWindowState.Minimized;
            }
        };
    }

    Settings EditedSettings() => (Settings)_grid.SelectedObject;

    void ApplySettings()
    {
        _settings = EditedSettings().Clone();
        _settings.Save();
        if (_engine.Running)
            _engine.Start(_settings);
        _status.Text = _engine.Status;
    }

    void ToggleRunning()
    {
        if (_engine.Running)
        {
            _engine.Stop();
            _startStop.Text = "Démarrer";
        }
        else
        {
            _settings = EditedSettings().Clone();
            _settings.Save();
            _engine.Start(_settings);
            _startStop.Text = "Arrêter";
        }
        _status.Text = _engine.Status;
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
