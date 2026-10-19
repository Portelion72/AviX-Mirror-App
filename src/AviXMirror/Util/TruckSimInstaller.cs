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

    // Résolution des rétroviseurs de l'interface (touche F2) : ce sont eux que la capture recopie sur le VoCore.
    static readonly (string Name, string Value)[] MirrorVariables =
    {
        ("r_mirror_scale_x", "2.0"),
        ("r_mirror_scale_y", "2.0"),
    };

    /// <summary>Fichiers « config.cfg » d'ETS2 et ATS (Documents).</summary>
    public static List<(string Name, string Config)> FindConfigs()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var result = new List<(string, string)>();
        foreach (var (name, folder) in GamesList)
        {
            var config = Path.Combine(documents, folder, "config.cfg");
            if (File.Exists(config))
                result.Add((name, config));
        }
        return result;
    }

    /// <summary>
    /// Rétros de l'interface rendus en résolution double (variables officielles de config.cfg), pour une image
    /// plus nette sur le VoCore. Une copie « config.cfg.avix-backup » est gardée. Le jeu doit être fermé.
    /// </summary>
    public static void SharpenMirrors(string config)
    {
        var backup = config + ".avix-backup";
        if (!File.Exists(backup))
            File.Copy(config, backup);
        var lines = File.ReadAllLines(config).ToList();
        foreach (var (name, value) in MirrorVariables)
        {
            var line = $"uset {name} \"{value}\"";
            int index = lines.FindIndex(l => l.TrimStart().StartsWith("uset " + name + " ", StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                lines[index] = line;
            else
                lines.Add(line);
        }
        File.WriteAllLines(config, lines);
    }

    /// <summary>Remet le config.cfg d'avant (copie de sauvegarde), s'il y en a une.</summary>
    public static bool RestoreConfig(string config)
    {
        var backup = config + ".avix-backup";
        if (!File.Exists(backup))
            return false;
        File.Copy(backup, config, overwrite: true);
        File.Delete(backup);
        return true;
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
