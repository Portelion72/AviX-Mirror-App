namespace AviXMirror.Radar;

/// <summary>
/// Catégories de voitures déjà vues dans la télémétrie des jeux (pour le menu déroulant des catégories
/// personnalisées des LEDs). Mémorisées dans %LOCALAPPDATA%\AviXMirror\categories.txt.
/// </summary>
public static class SeenClasses
{
    static readonly object Lock = new();
    static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
    static bool _loaded, _dirty;
    static DateTime _savedAt = DateTime.MinValue;

    /// <summary>Catégories connues d'avance, proposées même avant de les avoir croisées en jeu.</summary>
    static readonly string[] BuiltIn = { "Hypercar", "LMP2", "LMP3", "GTE", "LMGT3", "GT3", "GT4", "TCR" };

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AviXMirror", "categories.txt");

    static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                    if (!string.IsNullOrWhiteSpace(line))
                        Names.Add(line.Trim());
        }
        catch
        {
            // Fichier illisible : la liste se reconstruit en jeu.
        }
    }

    /// <summary>Note les catégories d'un état de course (enregistrées au plus toutes les 10 s).</summary>
    public static void Add(RadarWorld world)
    {
        lock (Lock)
        {
            EnsureLoaded();
            foreach (var v in world.Vehicles)
            {
                var name = v.Class?.Trim();
                if (!string.IsNullOrEmpty(name) && name.Length <= 40 && Names.Add(name))
                    _dirty = true;
            }
            if (_dirty && (DateTime.Now - _savedAt).TotalSeconds > 10)
                Save();
        }
    }

    static void Save()
    {
        _savedAt = DateTime.Now;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _dirty = false;
        }
        catch
        {
            // Dossier inaccessible : on réessaiera.
        }
    }

    /// <summary>Toutes les catégories vues (et celles connues d'avance), triées.</summary>
    public static List<string> All()
    {
        lock (Lock)
        {
            EnsureLoaded();
            return Names.Concat(BuiltIn).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
