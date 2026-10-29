namespace AviXMirror.Util;

/// <summary>
/// Fichiers de l'utilisateur (réglages, vibreurs) gardés dans %LOCALAPPDATA%\AviXMirror et non plus à côté de
/// l'exe : une mise à jour (nouveau zip extrait ailleurs, exe remplacé) ne les perd plus. Au premier lancement
/// d'une version qui ne les trouve pas encore là, le fichier le plus récent est repris à côté de l'exe, dans
/// l'ancienne installation (lancement avec Windows, raccourci du bureau) ou dans Téléchargements / Bureau /
/// Documents.
/// </summary>
public static class UserData
{
    static readonly object Lock = new();
    static readonly HashSet<string> Checked = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AviXMirror");

    /// <summary>Chemin du fichier dans le dossier de l'utilisateur (repris d'une ancienne installation s'il n'y est pas).</summary>
    public static string Path(string fileName)
    {
        var target = System.IO.Path.Combine(Folder, fileName);
        lock (Lock)
        {
            if (Checked.Add(fileName) && !File.Exists(target) && FindPrevious(fileName) is { } source)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.Copy(source, target, overwrite: false);
                }
                catch
                {
                    // Copie impossible : on repartira des valeurs par défaut.
                }
            }
        }
        return target;
    }

    /// <summary>Enregistre un fichier sans risque de le laisser à moitié écrit (fichier temporaire puis remplacement).</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Le plus récent des fichiers de ce nom laissés par une installation précédente.</summary>
    static string? FindPrevious(string fileName)
    {
        var found = new List<string>();
        foreach (var folder in PreviousFolders())
        {
            try
            {
                var file = System.IO.Path.Combine(folder, fileName);
                if (File.Exists(file))
                    found.Add(file);
            }
            catch
            {
                // Chemin invalide : ignoré.
            }
        }
        // Zips extraits dans les dossiers habituels (quelques niveaux seulement).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
                 {
                     System.IO.Path.Combine(home, "Downloads"),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                 })
        {
            try
            {
                if (Directory.Exists(root))
                    found.AddRange(Directory.EnumerateFiles(root, fileName, new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true,
                    }));
            }
            catch
            {
                // Dossier inaccessible : ignoré.
            }
        }
        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(f => { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } })
            .FirstOrDefault();
    }

    static IEnumerable<string> PreviousFolders()
    {
        yield return AppContext.BaseDirectory;
        foreach (var exe in WindowsIntegration.PreviousExePaths())
            if (System.IO.Path.GetDirectoryName(exe) is { Length: > 0 } folder)
                yield return folder;
    }
}
