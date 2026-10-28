namespace AviXMirror.Radar;

/// <summary>
/// Voitures déjà conduites, par jeu (pour le menu « Voiture » des profils, même jeu arrêté).
/// Mémorisées dans %LOCALAPPDATA%\AviXMirror\voitures.txt (lignes « Jeu|voiture »).
/// </summary>
public static class SeenCars
{
    static readonly object Lock = new();
    static readonly HashSet<string> Keys = new(StringComparer.Ordinal);
    static bool _loaded;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AviXMirror", "voitures.txt");

    static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                    if (line.Contains('|'))
                        Keys.Add(line.Trim());
        }
        catch
        {
            // Fichier illisible : la liste se reconstruit en jeu.
        }
    }

    /// <summary>Note la voiture conduite (enregistrée tout de suite : une seule fois par voiture).</summary>
    public static void Add(RadarGame game, string car)
    {
        lock (Lock)
        {
            EnsureLoaded();
            if (!Keys.Add(Settings.CarProfileKey(game, car)))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, Keys.OrderBy(k => k, StringComparer.Ordinal));
            }
            catch
            {
                // Dossier inaccessible : la voiture reste connue jusqu'à la fermeture.
            }
        }
    }

    /// <summary>Voitures déjà conduites dans ce jeu.</summary>
    public static List<string> For(RadarGame game)
    {
        lock (Lock)
        {
            EnsureLoaded();
            string prefix = game + "|";
            return Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]).ToList();
        }
    }
}
