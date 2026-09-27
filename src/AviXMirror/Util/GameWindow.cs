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
    /// Agrandit la fenêtre pour que sa zone de rendu couvre l'écran du jeu plus la bande, sans toucher
    /// à son style, puis signale la fin d'un redimensionnement (comme un étirement à la souris) pour
    /// que le jeu recalcule la taille de son rendu au lieu d'étirer l'image.
    /// Retourne vrai si la fenêtre a été modifiée.
    /// </summary>
    public static bool Extend(IntPtr hwnd, Settings s)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd))
            return false;

        var target = TargetRect(s);
        if (GetClientScreenRect(hwnd) == target)
            return false;

        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        var frame = new Native.RECT { Left = target.Left, Top = target.Top, Right = target.Right, Bottom = target.Bottom };
        Native.AdjustWindowRectEx(ref frame, (uint)style, false, (uint)ex);

        Native.SetWindowPos(hwnd, IntPtr.Zero, frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        Native.PostMessage(hwnd, Native.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    /// <summary>Zone de rendu de la fenêtre, en coordonnées écran.</summary>
    public static Rectangle GetClientScreenRect(IntPtr hwnd)
    {
        if (!Native.GetClientRect(hwnd, out var c))
            return Rectangle.Empty;
        var origin = new Point(0, 0);
        Native.ClientToScreen(hwnd, ref origin);
        return new Rectangle(origin.X, origin.Y, c.Right - c.Left, c.Bottom - c.Top);
    }

    public static Rectangle GetRect(IntPtr hwnd) =>
        Native.GetWindowRect(hwnd, out var r) ? r.ToRectangle() : Rectangle.Empty;
}
