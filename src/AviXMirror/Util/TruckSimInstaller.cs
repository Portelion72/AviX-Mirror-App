using System.Reflection;

namespace AviXMirror.Util;

/// <summary>
/// Installe le plugin de télémétrie AviX Mirror (SDK officiel SCS) dans Euro Truck Simulator 2 et
/// American Truck Simulator : « bin\win_x64\plugins\AviXMirrorScs.dll ».
/// </summary>
public static class TruckSimInstaller
{
    const string Resource = "TruckSim.AviXMirrorScs.dll";
    const string FileName = "AviXMirrorScs.dll";

    static readonly (string Name, string Folder)[] GamesList =
    {
        ("Euro Truck Simulator 2", "Euro Truck Simulator 2"),
        ("American Truck Simulator", "American Truck Simulator"),
    };

    /// <summary>Plugin présent dans cette version d'AviX Mirror (compilé par la CI).</summary>
    public static bool Available => Assembly.GetExecutingAssembly().GetManifestResourceInfo(Resource) != null;

    /// <summary>Jeux trouvés dans les bibliothèques Steam : nom et dossier « bin\win_x64 ».</summary>
    public static List<(string Name, string Bin)> FindGames()
    {
        var found = new List<(string, string)>();
        foreach (var library in AcInstaller.SteamLibraries())
            foreach (var (name, folder) in GamesList)
            {
                var bin = Path.Combine(library, "steamapps", "common", folder, "bin", "win_x64");
                if (Directory.Exists(bin) && !found.Any(f => string.Equals(f.Item2, bin, StringComparison.OrdinalIgnoreCase)))
                    found.Add((name, bin));
            }
        return found;
    }

    /// <summary>Copie le plugin dans « plugins » du dossier bin\win_x64 donné ; retourne le chemin du fichier.</summary>
    public static string Install(string bin)
    {
        var plugins = Path.Combine(bin, "plugins");
        Directory.CreateDirectory(plugins);
        var target = Path.Combine(plugins, FileName);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new FileNotFoundException("Le plugin n'est pas inclus dans cette version d'AviX Mirror.");
        using var output = File.Create(target);
        resource.CopyTo(output);
        return target;
    }
}
