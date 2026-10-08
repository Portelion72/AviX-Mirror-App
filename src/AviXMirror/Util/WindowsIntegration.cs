using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AviXMirror.Util;

/// <summary>Raccourci sur le bureau et lancement au démarrage de Windows (compte de l'utilisateur, sans droits administrateur).</summary>
public static class WindowsIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "AviXMirror";
    const string ShortcutName = "AviX Mirror.lnk";

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AviXMirror.exe");

    static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    // ---------- Démarrage avec Windows ----------

    public static bool StartsWithWindows
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Ajoute ou retire AviX Mirror des programmes lancés à l'ouverture de session.</summary>
    public static bool SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(RunName, $"\"{ExePath}\"");
            else if (key.GetValue(RunName) != null)
                key.DeleteValue(RunName, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Si le lancement automatique est actif, le met à jour avec l'emplacement actuel de l'exe (déplacé, mis à jour).</summary>
    public static void RefreshStartWithWindows()
    {
        if (StartsWithWindows)
            SetStartWithWindows(true);
    }

    // ---------- Raccourci sur le bureau ----------

    public static bool HasDesktopShortcut => File.Exists(ShortcutPath);

    /// <summary>Crée (ou met à jour) le raccourci « AviX Mirror » sur le bureau.</summary>
    public static bool CreateDesktopShortcut(Icon? icon = null)
    {
        try
        {
            // Icône AVIX enregistrée à part (l'exe n'a pas d'icône intégrée).
            string iconPath = ExePath;
            if (icon != null)
            {
                try
                {
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AviXMirror");
                    Directory.CreateDirectory(folder);
                    iconPath = Path.Combine(folder, "AviXMirror.ico");
                    using var file = File.Create(iconPath);
                    icon.Save(file);
                }
                catch
                {
                    iconPath = ExePath;
                }
            }
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(ExePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory);
            link.SetDescription("AviX Mirror — rétroviseur VoCore (AVIX_3D)");
            link.SetIconLocation(iconPath, 0);
            ((IPersistFile)link).Save(ShortcutPath, true);
            Marshal.ReleaseComObject(link);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
