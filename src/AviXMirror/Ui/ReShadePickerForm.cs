using AviXMirror.Capture;

namespace AviXMirror.Ui;

/// <summary>
/// Choix de l'image du rétroviseur parmi les textures que l'add-on ReShade a trouvées dans le jeu : une vignette
/// par texture, mise à jour en direct. Un clic choisit l'image 1 (rétro gauche), un second clic sur une autre
/// image ajoute l'image 2 (rétro droit) ; un clic sur une image choisie la retire.
/// </summary>
public sealed class ReShadePickerForm : Form
{
    readonly ReShadeLink _link = new();
    readonly FlowLayoutPanel _grid = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(14, 10, 14, 0) };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    uint _lastThumbSeq = uint.MaxValue;

    readonly List<string> _chosen;

    /// <summary>Clés des textures choisies, séparées par « | » (vide si aucune).</summary>
    public string Selected => string.Join("|", _chosen);

    public ReShadePickerForm(string current)
    {
        _chosen = ReShadeSource.Targets(current).ToList();
        Text = "Image du rétroviseur (ReShade)";
        Icon = Theme.CreateAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 640);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        _info.BackColor = Theme.Surface;
        _info.ForeColor = Theme.Text;
        _info.Font = Theme.Font(9.5f);
        _grid.BackColor = Theme.Background;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Surface, Padding = new Padding(14, 9, 14, 9) };
        var ok = new FlatButton { Text = "Valider", Primary = true, Width = 140, Dock = DockStyle.Right };
        var cancel = new FlatButton { Text = "Annuler", Width = 120, Dock = DockStyle.Right };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Theme.Surface });
        bottom.Controls.Add(ok);

        Controls.Add(_grid);
        Controls.Add(_info);
        Controls.Add(bottom);

        _timer.Tick += (_, _) => RefreshGrid();
        _timer.Start();
        RefreshGrid();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    void RefreshGrid()
    {
        if (!_link.Open())
        {
            _info.Text = "L'add-on AviX Mirror n'est pas chargé dans le jeu.\n" +
                         "ETS2 / ATS en solo, avec ReShade « add-ons » et AviXMirror.addon64 installés (Guide du jeu). Lancez le jeu et prenez le volant.";
            return;
        }
        _link.ThumbnailsWanted = true;
        uint seq = _link.ThumbSeq;
        if (seq == _lastThumbSeq)
            return;
        _lastThumbSeq = seq;

        var candidates = _link.Candidates();
        _info.Text = $"{candidates.Count} image(s) calculée(s) par le jeu. Au volant : cliquez sur l'image du rétro gauche (1), puis " +
                     "sur celle du rétro droit (2) si elle est séparée. Un rétro bouge avec la route derrière vous.\nChoix : " +
                     (_chosen.Count > 0 ? string.Join("  +  ", _chosen) : "aucun");

        _grid.SuspendLayout();
        var old = _grid.Controls.Cast<Control>().ToArray();
        _grid.Controls.Clear();
        foreach (var c in old)
        {
            foreach (var picture in c.Controls.OfType<PictureBox>())
                picture.Image?.Dispose();
            c.Dispose();
        }
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var thumb = _link.Thumbnail(i);
            int rank = _chosen.IndexOf(candidate.Key);
            bool chosen = rank >= 0;
            var tile = new Panel
            {
                Width = ReShadeLink.ThumbW + 16,
                Height = ReShadeLink.ThumbH + 44,
                Margin = new Padding(6),
                BackColor = chosen ? Theme.Accent : Theme.Surface,
                Padding = new Padding(8),
                Cursor = Cursors.Hand,
            };
            var picture = new PictureBox { Image = thumb, Dock = DockStyle.Top, Height = ReShadeLink.ThumbH, SizeMode = PictureBoxSizeMode.StretchImage, Cursor = Cursors.Hand };
            var label = new Label
            {
                Text = (chosen ? (rank == 0 ? "1 · gauche  —  " : "2 · droite  —  ") : "") + candidate.Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = chosen ? Color.Black : Theme.Text,
                Font = Theme.Font(8f),
                Cursor = Cursors.Hand,
            };
            void Choose(object? sender, EventArgs e)
            {
                if (_chosen.Contains(candidate.Key))
                    _chosen.Remove(candidate.Key);
                else if (_chosen.Count < 2)
                    _chosen.Add(candidate.Key);
                else
                    _chosen[1] = candidate.Key;
                _lastThumbSeq = uint.MaxValue;
                RefreshGrid();
            }
            tile.Click += Choose;
            picture.Click += Choose;
            label.Click += Choose;
            tile.Controls.Add(label);
            tile.Controls.Add(picture);
            _grid.Controls.Add(tile);
        }
        _grid.ResumeLayout();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _link.Dispose();
        base.OnFormClosed(e);
    }
}
