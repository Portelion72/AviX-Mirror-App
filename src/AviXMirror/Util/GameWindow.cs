using System.Diagnostics;

namespace AviXMirror.Util;

/// <summary>Recherche de la fenêtre de LMU.</summary>
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
}
