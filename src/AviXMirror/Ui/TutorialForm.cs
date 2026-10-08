using System.Drawing.Drawing2D;

namespace AviXMirror.Ui;

/// <summary>
/// Tutoriel de prise en main, affiché au premier démarrage (et à la demande par le bouton « Tutoriel »).
/// Une page par étape : illustration dessinée, titre, explication et, si utile, une action directe
/// (choisir le mode, installer l'app Assetto Corsa).
/// </summary>
public sealed class TutorialForm : Form
{
    sealed record Step(string Title, string Text, Action<Graphics, RectangleF> Illustration, Func<Control?>? Actions = null);

    readonly List<Step> _steps;
    readonly Illustration _picture = new() { Dock = DockStyle.Fill };
    readonly Label _title = new() { Dock = DockStyle.Top, Height = 42, AutoSize = false };
    readonly Label _text = new() { Dock = DockStyle.Fill, AutoSize = false };
    readonly Panel _actions = new() { Dock = DockStyle.Bottom, Height = 0 };
    readonly StepDots _dots = new() { Width = 160, Height = 20 };
    readonly FlatButton _previous = new() { Text = "Précédent", Width = 120, Height = 38 };
    readonly FlatButton _next = new() { Text = "Suivant", Primary = true, Width = 150, Height = 38 };
    readonly FlatButton _skip = new() { Text = "Passer le tutoriel", Width = 150, Height = 38 };
    readonly CheckBox _showAtStart = new() { Text = "Afficher au démarrage", AutoSize = true };
    readonly CheckBox _desktopShortcut = new() { Text = "Ajouter AviX Mirror sur le bureau (raccourci)", AutoSize = true };
    readonly CheckBox _startWithWindows = new() { Text = "Lancer AviX Mirror au démarrage de Windows", AutoSize = true };
    bool _optionsSeen;
    int _index;

    /// <summary>Vrai si l'utilisateur a vu la dernière étape et coché « Ajouter sur le bureau ».</summary>
    public bool CreateDesktopShortcut => _optionsSeen && _desktopShortcut.Checked;

    /// <summary>Choix « Lancer au démarrage de Windows » (null si la dernière étape n'a pas été vue).</summary>
    public bool? StartWithWindows => _optionsSeen ? _startWithWindows.Checked : null;

    /// <summary>Faux si l'utilisateur a décoché « Afficher au démarrage ».</summary>
    public bool ShowAtStartup => _showAtStart.Checked;

    /// <param name="currentMode">Mode actuellement choisi (mis en avant à l'étape des modes).</param>
    /// <param name="chooseMode">Appelé quand l'utilisateur choisit un mode dans le tutoriel.</param>
    /// <param name="installAcApp">Installe l'app Lua d'Assetto Corsa.</param>
    public TutorialForm(MirrorMode currentMode, bool showAtStartup, Action<MirrorMode> chooseMode, Action installAcApp,
        bool hasDesktopShortcut, bool startsWithWindows)
    {
        // Premier lancement : les deux options sont proposées cochées.
        _desktopShortcut.Checked = !hasDesktopShortcut;
        _startWithWindows.Checked = startsWithWindows || Settings.FirstRun;
        foreach (var box in new[] { _desktopShortcut, _startWithWindows })
        {
            box.ForeColor = Theme.Text;
            box.Font = Theme.Font(10f);
            box.Margin = new Padding(0, 4, 0, 4);
        }
        Text = "AviX Mirror — prise en main";
        Icon = Theme.CreateAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(880, 640);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        _showAtStart.Checked = showAtStartup;

        var mode = currentMode;
        _steps = new List<Step>
        {
            new("Bienvenue dans AviX Mirror",
                "AviX Mirror affiche un vrai rétroviseur sur votre écran VoCore 7,8\", pour Le Mans Ultimate et " +
                "Assetto Corsa, sans laisser le rétro virtuel sur l'écran principal.\n\n" +
                "Ce tutoriel vous guide en quelques étapes : branchement, choix du mode, réglages. " +
                "Vous pourrez le rouvrir à tout moment avec le bouton « Tutoriel ».",
                DrawWelcome),
            new("1. Brancher le VoCore",
                "Branchez le VoCore en USB. AviX Mirror lui envoie l'image directement, avec le pilote USB " +
                "installé par SimHub : pas de second écran Windows.\n\n" +
                "• Fermez SimHub (ou désactivez-y le VoCore) : un seul logiciel à la fois peut piloter l'écran.\n" +
                "• Désinstallez le pilote « écran » VoCore s'il est installé : il provoque l'erreur Easy Anti-Cheat 30007.\n" +
                "• Les 2 barrettes de LEDs s'allument une à une au branchement : c'est le test de câblage.",
                DrawVoCore),
            new("2. Choisir le mode",
                "Trois façons d'afficher ce qui est derrière vous. Cliquez sur un mode pour le choisir " +
                "(vous pourrez en changer à tout moment avec les tuiles à gauche de la fenêtre).",
                (g, r) => DrawModes(g, r, mode),
                () => ModeButtons(m => { mode = m; chooseMode(m); _picture.Invalidate(); })),
            new("3. Mode Radar",
                "Le plus simple : un rétro synthétique dessiné depuis la télémétrie, sans toucher au jeu.\n\n" +
                "• Le Mans Ultimate : activez le plugin « rF2 Shared Memory Map » (SimHub l'installe).\n" +
                "• Le tracé du circuit s'apprend tout seul pendant les premiers tours, puis il est mémorisé.\n" +
                "• Chaque voiture LMU a sa face avant ; la couleur indique sa catégorie.",
                DrawRadar),
            new("4. Assetto Corsa",
                "Pour le Radar et la Caméra AC, Assetto Corsa a besoin d'une petite app pour Custom Shaders Patch " +
                "(installé avec Content Manager).\n\n" +
                "• Cliquez sur le bouton ci-dessous : l'app est copiée dans le dossier d'Assetto Corsa.\n" +
                "• Elle démarre toute seule avec le jeu. Mode Caméra AC : vraie vue arrière rendue hors écran.\n" +
                "• Après chaque mise à jour d'AviX Mirror, réinstallez-la.",
                DrawAssetto,
                () => ActionButton("Installer l'app Assetto Corsa", installAcApp)),
            new("5. Autres jeux (beta)",
                "Le jeu lancé est détecté tout seul (« Jeu » = Auto) et chaque jeu a son profil de réglages. Le bouton « Guide du jeu » " +
                "(à côté du profil) explique la configuration de chaque jeu et applique des réglages de base conseillés.\n\n" +
                "• rFactor 2 : plugin « rF2 Shared Memory Map », comme LMU.\n" +
                "• Assetto Corsa Competizione et Assetto Corsa EVO : rien à configurer.\n" +
                "• Automobilista 2 / Project CARS 2 : Options → Système → Mémoire partagée = « Project CARS 2 ».\n" +
                "• iRacing : rien à configurer (radar simplifié : distance exacte, voitures à côté d'après le spotter d'iRacing).\n" +
                "• F1 23 / 24 / 25 : Réglages → Télémétrie → UDP activée, port 20777.",
                DrawOtherGames),
            new("6. Mode Capture du rétro",
                "La vraie image du rétro virtuel du jeu (LMU ; autres jeux en beta), recopiée sur le VoCore et cachée sur l'écran principal.\n\n" +
                "• Jeu en « Fenêtré » ou « Sans bordure », rétro virtuel placé dans un coin.\n" +
                "• Démarrez, attendez quelques secondes (Easy Anti-Cheat), puis « Calibrer la zone » : le cadre s'aimante sur les " +
                "bords du rétro (Alt pour placer librement).\n" +
                "• Points de couleur du cache : double-clic autour du cadre pour en ajouter, glisser pour déplacer, clic droit pour supprimer.",
                DrawCapture),
            new("7. ATH et LEDs",
                "Sur les 3 modes, un ATH façon caméra de recul : flèche au-dessus de chaque voiture (vert > 1 s, " +
                "orange > 0,5 s, rouge), échelles de distance et de temps sur les côtés.\n\n" +
                "Les LEDs s'allument quand une voiture arrive (couleur de sa catégorie), restent allumées quand elle " +
                "est à côté, et clignotent très vite en cas de dive bomb. Tout se règle dans les onglets ATH et LEDs.",
                DrawHudLeds),
            new("8. Veille, extinction et mises à jour",
                "• Jeu en pause ou dans les menus : animation AVIX sur le VoCore, cache retiré, LEDs éteintes.\n" +
                "• Retour sur le bureau : logo AVIX.\n" +
                "• Fermeture d'AviX Mirror, extinction ou veille du PC : l'écran s'éteint complètement.\n" +
                "• Une nouvelle version ? Un bandeau apparaît en haut de la fenêtre.\n\n" +
                "C'est tout ! Choisissez ci-dessous, cliquez sur « Terminer », puis sur DÉMARRER.",
                DrawStandby,
                InstallOptions),
        };

        _picture.Draw = (g, r) => _steps[_index].Illustration(g, r);
        _title.Font = Theme.Font(16f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _text.Font = Theme.Font(10.5f);
        _text.ForeColor = Theme.Blend(Theme.Text, Theme.TextMuted, 0.25f);
        _showAtStart.ForeColor = Theme.TextMuted;
        _showAtStart.Font = Theme.Font(9f);

        var illustrationCard = new Card { Dock = DockStyle.Top, Height = 280, Padding = new Padding(2) };
        illustrationCard.Controls.Add(_picture);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 12, 28, 8), BackColor = Theme.Background };
        content.Controls.Add(_text);
        content.Controls.Add(_actions);
        content.Controls.Add(_title);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Surface, Padding = new Padding(20, 13, 20, 13) };
        _next.Dock = DockStyle.Right;
        _previous.Dock = DockStyle.Right;
        _skip.Dock = DockStyle.Left;
        var spacer = new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Surface };
        var middle = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(20, 8, 0, 0), WrapContents = false };
        middle.Controls.Add(_dots);
        middle.Controls.Add(_showAtStart);
        bottom.Controls.Add(middle);
        bottom.Controls.Add(_previous);
        bottom.Controls.Add(spacer);
        bottom.Controls.Add(_next);
        bottom.Controls.Add(_skip);

        var top = new Panel { Dock = DockStyle.Top, Height = 296, Padding = new Padding(20, 16, 20, 0), BackColor = Theme.Background };
        top.Controls.Add(illustrationCard);

        Controls.Add(content);
        Controls.Add(top);
        Controls.Add(bottom);

        _next.Click += (_, _) => { if (_index < _steps.Count - 1) ShowStep(_index + 1); else Finish(); };
        _previous.Click += (_, _) => { if (_index > 0) ShowStep(_index - 1); };
        _skip.Click += (_, _) => Finish();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Right) _next.PerformClick();
            else if (e.KeyCode == Keys.Left && _index > 0) ShowStep(_index - 1);
            else if (e.KeyCode == Keys.Escape) Finish();
        };

        ShowStep(0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    void ShowStep(int index)
    {
        _index = index;
        var step = _steps[index];
        _title.Text = step.Title;
        _text.Text = step.Text;
        _actions.Controls.Clear();
        var actions = step.Actions?.Invoke();
        if (actions != null)
        {
            int height = actions.Height;
            actions.Dock = DockStyle.Fill;
            _actions.Height = height + 8;
            _actions.Controls.Add(actions);
        }
        else
        {
            _actions.Height = 0;
        }
        _previous.Visible = index > 0;
        _next.Text = index == _steps.Count - 1 ? "Terminer" : "Suivant";
        _skip.Visible = index < _steps.Count - 1;
        _dots.Set(index, _steps.Count);
        _picture.Invalidate();
    }

    void Finish()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    Control ModeButtons(Action<MirrorMode> choose)
    {
        var panel = new TableLayoutPanel { Height = 46, ColumnCount = 3, BackColor = Theme.Background };
        for (int i = 0; i < 3; i++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        foreach (var (m, label) in new[] { (MirrorMode.Radar, "Radar"), (MirrorMode.CameraAssettoCorsa, "Caméra Assetto Corsa"), (MirrorMode.Capture, "Capture du rétro") })
        {
            var button = new FlatButton { Text = label, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 10, 4) };
            button.Click += (_, _) => choose(m);
            panel.Controls.Add(button);
        }
        return panel;
    }

    /// <summary>Dernière étape : raccourci sur le bureau et lancement avec Windows.</summary>
    Control InstallOptions()
    {
        _optionsSeen = true;
        var panel = new FlowLayoutPanel { Height = 66, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Background };
        panel.Controls.Add(_desktopShortcut);
        panel.Controls.Add(_startWithWindows);
        return panel;
    }

    static Control ActionButton(string text, Action action)
    {
        var panel = new Panel { Height = 46, BackColor = Theme.Background };
        var button = new FlatButton { Text = text, Width = 280, Height = 38, Location = new Point(0, 4) };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
        return panel;
    }

    // ---------- Illustrations ----------

    static void DrawWelcome(Graphics g, RectangleF r)
    {
        using var bmp = new Bitmap((int)r.Width, (int)r.Height);
        Splash.Draw(bmp, "Rétroviseur VoCore · Le Mans Ultimate · Assetto Corsa");
        g.DrawImage(bmp, r.Location);
    }

    /// <summary>Écran VoCore au format 1280×400, avec les 2 barrettes de LEDs, dans un cadre de rétroviseur.</summary>
    static RectangleF DrawMirror(Graphics g, RectangleF area, Action<Graphics, RectangleF>? content = null, Color[]? leds = null)
    {
        float w = Math.Min(area.Width * 0.72f, area.Height * 0.62f * 3.2f), h = w / 3.2f;
        var screen = new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        using (var frame = Theme.RoundedRect(RectangleF.Inflate(screen, 14, 12), 18))
        using (var frameBrush = new SolidBrush(Color.FromArgb(40, 42, 48)))
        using (var framePen = new Pen(Theme.Border, 2))
        {
            g.FillPath(frameBrush, frame);
            g.DrawPath(framePen, frame);
        }
        using (var black = new SolidBrush(Color.Black))
            g.FillRectangle(black, screen);
        if (content != null)
        {
            var state = g.Save();
            g.SetClip(screen);
            content(g, screen);
            g.Restore(state);
        }
        if (leds != null)
        {
            int n = leds.Length / 2;
            for (int side = 0; side < 2; side++)
            for (int i = 0; i < n; i++)
            {
                float y = screen.Bottom - (i + 1) * screen.Height / n + 2;
                float x = side == 0 ? screen.Left - 11 : screen.Right + 5;
                using var led = new SolidBrush(leds[side * n + i]);
                g.FillEllipse(led, x, y, 6, screen.Height / n - 4);
            }
        }
        return screen;
    }

    static void DrawVoCore(Graphics g, RectangleF r)
    {
        var accent = Theme.Accent;
        var leds = Enumerable.Range(0, 16).Select(i => i % 8 < 3 ? accent : Color.FromArgb(50, 50, 55)).ToArray();
        var screen = DrawMirror(g, r, (gg, s) =>
        {
            // Dessiné dans une image à la taille de l'écran, puis posé dessus (Splash dessine depuis l'origine).
            using var bmp = new Bitmap(Math.Max(1, (int)s.Width), Math.Max(1, (int)s.Height));
            Splash.Draw(bmp, "");
            gg.DrawImage(bmp, s.Location);
        }, leds);
        // Câble USB vers le PC.
        using var cable = new Pen(Theme.TextMuted, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var start = new PointF(screen.Right + 16, screen.Bottom);
        g.DrawBezier(cable, start, new PointF(start.X + 60, start.Y + 10), new PointF(r.Right - 110, r.Bottom - 30), new PointF(r.Right - 70, r.Bottom - 30));
        using var font = Theme.Font(9f, FontStyle.Bold);
        TextRenderer.DrawText(g, "USB", font, new Point((int)r.Right - 64, (int)r.Bottom - 40), Theme.Accent);
    }

    static void DrawModes(Graphics g, RectangleF r, MirrorMode selected)
    {
        var modes = new (MirrorMode Mode, string Title, string Sub, Action<Graphics, RectangleF> Draw)[]
        {
            (MirrorMode.Radar, "RADAR", "LMU · Assetto Corsa", MiniRadar),
            (MirrorMode.CameraAssettoCorsa, "CAMÉRA AC", "vraie vue arrière", MiniCamera),
            (MirrorMode.Capture, "CAPTURE LMU", "rétro virtuel du jeu", MiniCapture),
        };
        float gap = 18, w = (r.Width - gap * 4) / 3, h = r.Height - 40;
        using var titleFont = Theme.Font(11f, FontStyle.Bold);
        using var subFont = Theme.Font(8.5f);
        for (int i = 0; i < modes.Length; i++)
        {
            var tile = new RectangleF(r.X + gap + i * (w + gap), r.Y + 20, w, h);
            bool active = modes[i].Mode == selected;
            using (var path = Theme.RoundedRect(tile, 10))
            using (var fill = new SolidBrush(active ? Theme.Blend(Theme.SurfaceRaised, Theme.Accent, 0.12f) : Theme.SurfaceRaised))
            using (var pen = new Pen(active ? Theme.Accent : Theme.Border, active ? 2 : 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            var preview = new RectangleF(tile.X + 14, tile.Y + 14, tile.Width - 28, (tile.Width - 28) / 3.2f);
            using (var black = new SolidBrush(Color.Black))
                g.FillRectangle(black, preview);
            var state = g.Save();
            g.SetClip(preview);
            modes[i].Draw(g, preview);
            g.Restore(state);
            TextRenderer.DrawText(g, modes[i].Title, titleFont, new Point((int)tile.X + 14, (int)preview.Bottom + 14), active ? Theme.Accent : Theme.Text);
            TextRenderer.DrawText(g, modes[i].Sub, subFont, new Point((int)tile.X + 14, (int)preview.Bottom + 38), Theme.TextMuted);
        }
    }

    static void MiniRadar(Graphics g, RectangleF r)
    {
        using (var sky = new LinearGradientBrush(r, Color.FromArgb(30, 34, 44), Color.FromArgb(10, 10, 14), LinearGradientMode.Vertical))
            g.FillRectangle(sky, r);
        float horizon = r.Y + r.Height * 0.35f;
        using (var road = new SolidBrush(Color.FromArgb(58, 60, 66)))
            g.FillPolygon(road, new[] { new PointF(r.X + r.Width * 0.46f, horizon), new PointF(r.X + r.Width * 0.54f, horizon), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) });
        // Vibreurs rouge/blanc sur les bords.
        for (int i = 0; i < 8; i++)
        {
            float t0 = i / 8f, t1 = (i + 1) / 8f;
            float y0 = horizon + (r.Bottom - horizon) * t0, y1 = horizon + (r.Bottom - horizon) * t1;
            float l0 = r.X + r.Width * 0.46f * (1 - t0), l1 = r.X + r.Width * 0.46f * (1 - t1);
            using var kerb = new SolidBrush(i % 2 == 0 ? Color.FromArgb(210, 40, 40) : Color.WhiteSmoke);
            g.FillPolygon(kerb, new[] { new PointF(l0, y0), new PointF(l0 + 4 + 6 * t0, y0), new PointF(l1 + 4 + 6 * t1, y1), new PointF(l1, y1) });
        }
        MiniCar(g, r.X + r.Width * 0.44f, r.Y + r.Height * 0.62f, r.Width * 0.16f, Color.FromArgb(215, 35, 45));
        MiniCar(g, r.X + r.Width * 0.60f, r.Y + r.Height * 0.46f, r.Width * 0.08f, Color.FromArgb(30, 165, 80));
    }

    static void MiniCar(Graphics g, float cx, float cy, float w, Color color)
    {
        float h = w * 0.5f;
        using var body = new SolidBrush(color);
        g.FillPolygon(body, new[]
        {
            new PointF(cx - w / 2, cy + h / 2), new PointF(cx - w / 2, cy), new PointF(cx - w * 0.3f, cy - h * 0.2f),
            new PointF(cx - w * 0.12f, cy - h / 2), new PointF(cx + w * 0.12f, cy - h / 2), new PointF(cx + w * 0.3f, cy - h * 0.2f),
            new PointF(cx + w / 2, cy), new PointF(cx + w / 2, cy + h / 2),
        });
        using var glass = new SolidBrush(Color.FromArgb(20, 22, 30));
        g.FillRectangle(glass, cx - w * 0.1f, cy - h * 0.4f, w * 0.2f, h * 0.3f);
        using var light = new SolidBrush(Color.FromArgb(255, 250, 215));
        g.FillRectangle(light, cx - w * 0.44f, cy, w * 0.14f, h * 0.1f);
        g.FillRectangle(light, cx + w * 0.3f, cy, w * 0.14f, h * 0.1f);
    }

    static void MiniCamera(Graphics g, RectangleF r)
    {
        using (var sky = new LinearGradientBrush(r, Color.FromArgb(120, 160, 205), Color.FromArgb(200, 215, 230), LinearGradientMode.Vertical))
            g.FillRectangle(sky, r);
        float horizon = r.Y + r.Height * 0.45f;
        using (var grass = new SolidBrush(Color.FromArgb(70, 120, 60)))
            g.FillRectangle(grass, r.X, horizon, r.Width, r.Bottom - horizon);
        using (var road = new SolidBrush(Color.FromArgb(80, 80, 85)))
            g.FillPolygon(road, new[] { new PointF(r.X + r.Width * 0.45f, horizon), new PointF(r.X + r.Width * 0.55f, horizon), new PointF(r.Right - r.Width * 0.1f, r.Bottom), new PointF(r.X + r.Width * 0.1f, r.Bottom) });
        MiniCar(g, r.X + r.Width * 0.5f, r.Y + r.Height * 0.7f, r.Width * 0.2f, Color.FromArgb(30, 90, 200));
    }

    static void MiniCapture(Graphics g, RectangleF r)
    {
        MiniCamera(g, r);
        using var tint = new SolidBrush(Color.FromArgb(40, 255, 200, 0));
        g.FillRectangle(tint, r);
    }

    static void DrawRadar(Graphics g, RectangleF r)
    {
        var leds = Enumerable.Repeat(Color.FromArgb(50, 50, 55), 16).ToArray();
        for (int i = 8; i < 12; i++)
            leds[i] = Color.FromArgb(255, 30, 30);
        DrawMirror(g, r, (gg, s) =>
        {
            MiniRadar(gg, s);
            DrawHudOverlay(gg, s, new[] { (0.44f, 0.46f, Color.FromArgb(255, 170, 20)), (0.60f, 0.40f, Color.FromArgb(70, 220, 90)) });
        }, leds);
    }

    static void DrawAssetto(Graphics g, RectangleF r)
    {
        var screen = DrawMirror(g, r, (gg, s) => MiniCamera(gg, s));
        using var font = Theme.Font(9f, FontStyle.Bold);
        using var badge = Theme.RoundedRect(new RectangleF(screen.X, screen.Bottom + 22, 250, 30), 8);
        using var fill = new SolidBrush(Theme.SurfaceRaised);
        using var pen = new Pen(Theme.Accent);
        g.FillPath(fill, badge);
        g.DrawPath(pen, badge);
        TextRenderer.DrawText(g, "Content Manager + Custom Shaders Patch", font, Rectangle.Round(new RectangleF(screen.X, screen.Bottom + 22, 250, 30)),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    static void DrawOtherGames(Graphics g, RectangleF r)
    {
        var games = new[] { "Le Mans Ultimate", "Assetto Corsa", "rFactor 2", "ACC", "AC EVO", "Automobilista 2", "iRacing", "F1 23 · 24 · 25" };
        using var font = Theme.Font(11f, FontStyle.Bold);
        using var small = Theme.Font(7.5f, FontStyle.Bold);
        int columns = 4;
        float gap = 16, w = (r.Width - gap * (columns + 1)) / columns, h = 62;
        float top = r.Y + (r.Height - (h * 2 + gap)) / 2;
        for (int i = 0; i < games.Length; i++)
        {
            var box = new RectangleF(r.X + gap + (i % columns) * (w + gap), top + (i / columns) * (h + gap), w, h);
            bool beta = i >= 2;
            using (var path = Theme.RoundedRect(box, 10))
            using (var fill = new SolidBrush(Theme.SurfaceRaised))
            using (var pen = new Pen(beta ? Theme.Border : Theme.Accent, beta ? 1 : 2))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, games[i], font, Rectangle.Round(box), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (beta)
            {
                var tag = new RectangleF(box.Right - 44, box.Y + 6, 38, 16);
                using var tagPath = Theme.RoundedRect(tag, 6);
                using var tagFill = new SolidBrush(Theme.Accent);
                g.FillPath(tagFill, tagPath);
                TextRenderer.DrawText(g, "BETA", small, Rectangle.Round(tag), Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    static void DrawCapture(Graphics g, RectangleF r)
    {
        // Écran du jeu, avec le rétro virtuel, le cadre de capture et les repères d'alignement.
        float w = Math.Min(r.Width * 0.62f, r.Height * 0.86f * 16 / 9f), h = w * 9 / 16f;
        var game = new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        MiniCamera(g, game);
        using (var dash = new SolidBrush(Color.FromArgb(25, 25, 30)))
            g.FillRectangle(dash, game.X, game.Y + h * 0.72f, w, h * 0.28f);
        var mirror = new RectangleF(game.X + w * 0.33f, game.Y + h * 0.07f, w * 0.34f, w * 0.34f / 3.2f);
        using (var mirrorFrame = new SolidBrush(Color.FromArgb(20, 20, 24)))
            g.FillRectangle(mirrorFrame, RectangleF.Inflate(mirror, 4, 4));
        MiniRadar(g, mirror);
        using (var guide = new Pen(Color.FromArgb(230, 255, 0, 200), 1.5f) { DashStyle = DashStyle.Dash })
        {
            g.DrawLine(guide, mirror.X + mirror.Width / 2, game.Top, mirror.X + mirror.Width / 2, game.Bottom);
            g.DrawLine(guide, game.Left, mirror.Top, game.Right, mirror.Top);
        }
        using (var frame = new Pen(Theme.Accent, 2))
            g.DrawRectangle(frame, mirror.X, mirror.Y, mirror.Width, mirror.Height);
        using var ring = new Pen(Color.White, 2);
        foreach (var (px, py) in new[] { (mirror.X + mirror.Width * 0.2f, mirror.Top - 9), (mirror.X + mirror.Width * 0.8f, mirror.Top - 9), (mirror.Right + 9, mirror.Y + mirror.Height / 2) })
        {
            using var dot = new SolidBrush(Color.FromArgb(120, 160, 205));
            g.FillEllipse(dot, px - 5, py - 5, 10, 10);
            g.DrawEllipse(ring, px - 5, py - 5, 10, 10);
        }
    }

    static void DrawHudOverlay(Graphics g, RectangleF s, (float X, float Y, Color C)[] arrows)
    {
        float band = s.Width * 0.07f;
        using var bg = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        g.FillRectangle(bg, s.X, s.Y, band, s.Height);
        g.FillRectangle(bg, s.Right - band, s.Y, band, s.Height);
        using var tick = new Pen(Color.FromArgb(230, 240, 240, 240), 1.5f);
        for (int i = 0; i <= 10; i++)
        {
            float y = s.Bottom - 3 - i * (s.Height - 6) / 10;
            g.DrawLine(tick, s.X + 3, y, s.X + band * 0.5f, y);
            g.DrawLine(tick, s.Right - 3, y, s.Right - band * 0.5f, y);
        }
        foreach (var (x, y, c) in arrows)
        {
            float cx = s.X + s.Width * x, cy = s.Y + s.Height * y, size = s.Height * 0.12f;
            using var brush = new SolidBrush(c);
            g.FillPolygon(brush, new[] { new PointF(cx - size / 2, cy - size), new PointF(cx, cy - size * 0.45f), new PointF(cx + size / 2, cy - size), new PointF(cx, cy) });
            g.FillPolygon(brush, new[] { new PointF(s.X + band, s.Y + s.Height * (0.2f + y) - 5), new PointF(s.X + band + 7, s.Y + s.Height * (0.2f + y)), new PointF(s.X + band, s.Y + s.Height * (0.2f + y) + 5) });
        }
    }

    static void DrawHudLeds(Graphics g, RectangleF r)
    {
        var leds = Enumerable.Repeat(Color.FromArgb(50, 50, 55), 16).ToArray();
        for (int i = 0; i < 5; i++)
            leds[i] = Color.FromArgb(30, 90, 255); // LMP2 qui arrive à droite
        for (int i = 8; i < 16; i++)
            leds[i] = Color.FromArgb(0, 200, 80);  // GT3 à côté à gauche
        DrawMirror(g, r, (gg, s) =>
        {
            MiniCamera(gg, s);
            DrawHudOverlay(gg, s, new[] { (0.5f, 0.55f, Color.FromArgb(255, 60, 45)) });
        }, leds);
    }

    static void DrawStandby(Graphics g, RectangleF r)
    {
        var screen = DrawMirror(g, r, (gg, s) =>
        {
            using var bmp = new Bitmap(Math.Max(1, (int)s.Width), Math.Max(1, (int)s.Height));
            Splash.DrawAnimated(bmp, 0.9, "Pause");
            gg.DrawImage(bmp, s.Location);
        });
        using var banner = Theme.RoundedRect(new RectangleF(r.X + 40, r.Y + 14, r.Width - 80, 30), 8);
        using var fill = new SolidBrush(Theme.Accent);
        g.FillPath(fill, banner);
        using var font = Theme.Font(9f, FontStyle.Bold);
        TextRenderer.DrawText(g, "NOUVELLE VERSION DISPONIBLE — CLIQUEZ POUR LA TÉLÉCHARGER", font,
            Rectangle.Round(new RectangleF(r.X + 40, r.Y + 14, r.Width - 80, 30)), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Zone d'illustration (dessinée à la demande).</summary>
    sealed class Illustration : Control
    {
        public Action<Graphics, RectangleF>? Draw;

        public Illustration()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            e.Graphics.Clear(Theme.Surface);
            try
            {
                Draw?.Invoke(e.Graphics, new RectangleF(0, 0, Width, Height));
            }
            catch
            {
                // Une illustration ne doit jamais empêcher le tutoriel de s'afficher.
            }
        }
    }

    /// <summary>Points d'avancement (une pastille par étape).</summary>
    sealed class StepDots : Control
    {
        int _index, _count;

        public StepDots()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = new Padding(0, 4, 16, 0);
        }

        public void Set(int index, int count)
        {
            _index = index;
            _count = count;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            for (int i = 0; i < _count; i++)
            {
                bool active = i == _index;
                using var brush = new SolidBrush(active ? Theme.Accent : i < _index ? Theme.Blend(Theme.Accent, Theme.Surface, 0.5f) : Theme.Border);
                float w = active ? 22 : 8;
                float x = i * 14 + (i > _index ? 14 : 0);
                using var path = Theme.RoundedRect(new RectangleF(x, 6, w, 8), 4);
                e.Graphics.FillPath(brush, path);
            }
        }
    }
}
