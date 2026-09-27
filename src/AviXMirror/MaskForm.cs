using AviXMirror.Util;

namespace AviXMirror;

/// <summary>
/// Cache noir posé sur le rétro virtuel de LMU, sur l'écran principal.
/// Il laisse passer les clics et n'apparaît pas dans la capture (qui lit la fenêtre du jeu).
/// </summary>
public sealed class MaskForm : Form
{
    public MaskForm()
    {
        Text = "AviX Mirror — cache";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW |
                                Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
    }

    /// <summary>Place le cache sur <paramref name="bounds"/> (coordonnées écran) et le garde au premier plan.</summary>
    public void Cover(Rectangle bounds)
    {
        if (Bounds != bounds)
            Bounds = bounds;
        if (!Visible)
            Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
