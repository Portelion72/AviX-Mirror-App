using System.Diagnostics;

namespace AviXMirror.Util;

/// <summary>Recherche de la fenêtre de LMU et extension de celle-ci au-delà de l'écran.</summary>
public static class GameWindow
{
    public static IntPtr Find(string processName)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                    return p.MainWindowHandle;
            }
            catch
            {
                // Processus terminé entre-temps.
            }
            finally
            {
                p.Dispose();
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Rectangle visé pour la fenêtre du jeu, bande comprise.</summary>
    public static Rectangle TargetRect(Settings s)
    {
        var screen = ScreenHelper.Find(s.GameScreen) ?? Screen.PrimaryScreen!;
        var b = screen.Bounds;
        int strip = Math.Max(0, s.ExtensionPixels);
        return s.ExtensionSide == StripSide.Haut
            ? new Rectangle(b.X, b.Y - strip, b.Width, b.Height + strip)
            : new Rectangle(b.X, b.Y, b.Width, b.Height + strip);
    }

    /// <summary>
    /// Passe la fenêtre en style sans bordure et l'agrandit pour couvrir l'écran du jeu plus la bande.
    /// Retourne vrai si la fenêtre a été modifiée.
    /// </summary>
    public static bool Extend(IntPtr hwnd, Settings s)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd))
            return false;

        var target = TargetRect(s);
        bool changed = false;

        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        long wantedStyle = (style & ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU |
                                      Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX)) | Native.WS_POPUP;
        if (wantedStyle != style)
        {
            Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(wantedStyle));
            changed = true;
        }

        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        long wantedEx = ex & ~(Native.WS_EX_DLGMODALFRAME | Native.WS_EX_WINDOWEDGE |
                               Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE);
        if (wantedEx != ex)
        {
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(wantedEx));
            changed = true;
        }

        Native.GetWindowRect(hwnd, out var r);
        if (changed || r.ToRectangle() != target)
        {
            Native.SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_FRAMECHANGED);
            changed = true;
        }
        return changed;
    }

    public static Rectangle GetRect(IntPtr hwnd) =>
        Native.GetWindowRect(hwnd, out var r) ? r.ToRectangle() : Rectangle.Empty;
}
