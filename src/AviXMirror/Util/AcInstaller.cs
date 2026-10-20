using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AviXMirror.Util;

/// <summary>Installe l'app Lua « AviX Mirror » (Custom Shaders Patch) dans le dossier d'Assetto Corsa.</summary>
public static class AcInstaller
{
    static readonly string[] Files = { "manifest.ini", "AviXMirror.lua" };

    /// <summary>Cherche Assetto Corsa dans les bibliothèques Steam.</summary>
    public static string? FindAcFolder()
    {
        foreach (var library in SteamLibraries())
        {
            var folder = Path.Combine(library, "steamapps", "common", "assettocorsa");
            if (File.Exists(Path.Combine(folder, "AssettoCorsa.exe")))
                return folder;
        }
        return null;
    }

    static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path)
                roots.Add(path.Replace('/', '\\'));
        }
        catch { }
        roots.Add(@"C:\Program Files (x86)\Steam");

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            string text;
            try { text = File.ReadAllText(vdf); } catch { continue; }
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    /// <summary>Copie l'app dans « apps\lua\AviXMirror » et retourne le dossier créé.</summary>
    public static string Install(string acFolder)
    {
        var target = Path.Combine(acFolder, "apps", "lua", "AviXMirror");
        Directory.CreateDirectory(target);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var file in Files)
        {
            using var resource = assembly.GetManifestResourceStream("AcApp." + file)
                ?? throw new FileNotFoundException("Ressource manquante : " + file);
            using var output = File.Create(Path.Combine(target, file));
            resource.CopyTo(output);
        }
        return target;
    }
}
