namespace AviXMirror.Ui;

/// <summary>
/// Guide de configuration par jeu : réglages à faire dans le jeu, étapes dans AviX Mirror et réglages
/// de base conseillés, applicables d'un clic au profil du jeu.
/// </summary>
public sealed class GameGuideForm : Form
{
    readonly ListBox _games = new() { Dock = DockStyle.Left, Width = 250, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 16, 24, 16) };
    readonly FlatButton _apply = new() { Primary = true, Width = 300, Height = 38, Dock = DockStyle.Right };
    readonly Action<RadarGame> _applyRecommended;

    public GameGuideForm(RadarGame? initial, Action<RadarGame> applyRecommended)
    {
        _applyRecommended = applyRecommended;
        Text = "Configuration par jeu";
        Icon = Theme.CreateAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(800, 500);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        _games.BackColor = Theme.Surface;
        _games.ForeColor = Theme.Text;
        _games.Font = Theme.Font(10.5f);
        _games.DrawMode = DrawMode.OwnerDrawFixed;
        _games.ItemHeight = 40;
        foreach (var game in Radar.Games.All)
            _games.Items.Add(game);
        _games.DrawItem += DrawGame;
        _games.SelectedIndexChanged += (_, _) => ShowGuide();
        _content.BackColor = Theme.Background;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Surface, Padding = new Padding(16, 11, 16, 11) };
        var close = new FlatButton { Text = "Fermer", Width = 120, Height = 38, Dock = DockStyle.Left };
        close.Click += (_, _) => Close();
        _apply.Click += (_, _) =>
        {
            if (_games.SelectedItem is not Radar.GameInfo game)
                return;
            _applyRecommended(game.Game);
            MessageBox.Show(this, $"Réglages conseillés appliqués au profil « {game.Name} ».\n\n" +
                                  "Pensez ensuite à « Calibrer la zone » et « Aligner les flèches » en jeu (mode Capture).",
                "Configuration par jeu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        bottom.Controls.Add(_apply);
        bottom.Controls.Add(close);

        Controls.Add(_content);
        Controls.Add(_games);
        Controls.Add(bottom);

        int index = initial is { } g ? Array.FindIndex(Radar.Games.All, x => x.Game == g) : 0;
        _games.SelectedIndex = Math.Max(0, index);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    void DrawGame(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;
        var game = (Radar.GameInfo)_games.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Blend(Theme.Surface, Theme.Accent, 0.18f) : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
        }
        var text = new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 70, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, game.Name, _games.Font, text, selected ? Theme.Accent : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (game.Beta)
        {
            using var small = Theme.Font(7.5f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "BETA", small, new Rectangle(e.Bounds.Right - 54, e.Bounds.Y, 46, e.Bounds.Height),
                Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    void ShowGuide()
    {
        if (_games.SelectedItem is not Radar.GameInfo game)
            return;
        var guide = GameGuides.Get(game.Game);
        _apply.Text = "Appliquer les réglages conseillés";

        _content.SuspendLayout();
        var old = _content.Controls.Cast<Control>().ToArray();
        _content.Controls.Clear();
        foreach (var c in old)
            c.Dispose();

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Location = new Point(_content.Padding.Left, _content.Padding.Top),
        };
        int width = Math.Max(400, _content.ClientSize.Width - _content.Padding.Horizontal - 20);

        void AddText(string text, float size, Color color, FontStyle style = FontStyle.Regular, int top = 0) =>
            flow.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Font = Theme.Font(size, style),
                ForeColor = color,
                Margin = new Padding(0, top, 0, 4),
            });

        void Steps(string title, string[] steps)
        {
            AddText(title.ToUpperInvariant(), 9f, Theme.Accent, FontStyle.Bold, 14);
            for (int i = 0; i < steps.Length; i++)
                AddText($"{i + 1}.  {steps[i]}", 10f, Theme.Text);
        }

        AddText(game.Name + (game.Beta ? "  (beta)" : ""), 16f, Theme.Text, FontStyle.Bold);
        AddText(guide.Summary, 10f, Theme.TextMuted);
        Steps("Dans le jeu", guide.InGame);
        Steps("Dans AviX Mirror", guide.InAviX);
        AddText("RÉGLAGES DE BASE CONSEILLÉS", 9f, Theme.Accent, FontStyle.Bold, 14);
        foreach (var (_, _, label) in guide.Recommended)
            AddText("•  " + label, 10f, Theme.Text);
        AddText("Ils s'enregistrent dans le profil de ce jeu (les autres jeux ne changent pas). Rien n'a été testé en jeu " +
             "pour les titres en beta : signalez ce qui ne marche pas.", 8.5f, Theme.TextMuted, top: 10);

        _content.Controls.Add(flow);
        _content.ResumeLayout();
        _content.AutoScrollPosition = Point.Empty;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated)
            ShowGuide();
    }
}
