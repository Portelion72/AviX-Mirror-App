using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AviXMirror.Radar;

/// <summary>
/// Couleurs des vibreurs selon le circuit. Les valeurs par défaut sont écrites au premier lancement dans
/// « AviXMirror.kerbs.json » (à côté de l'exe) : chaque entrée associe un mot-clé du nom du circuit à une
/// suite de couleurs qui se répètent le long du vibreur. Le fichier peut être modifié librement.
/// </summary>
public static class KerbPalette
{
    const string Red = "#D42020", White = "#F0F0F0";

    static readonly (string[] Keys, string[] Colors)[] Defaults =
    {
        // Le Mans Ultimate — jeu de base
        (new[] { "sarthe", "le mans", "lemans", "le_mans" }, new[] { "#F2C200", "#1C4FC4" }),
        (new[] { "monza" }, new[] { "#1E9A3A", White, "#D42020" }),
        (new[] { "spa", "francorchamps" }, new[] { Red, White }),
        (new[] { "sebring" }, new[] { Red, White }),
        (new[] { "portimao", "algarve" }, new[] { Red, White }),
        (new[] { "bahrain", "sakhir" }, new[] { Red, White }),
        (new[] { "fuji" }, new[] { Red, White }),
        // Le Mans Ultimate — DLC WEC, ELMS et US Track Pass
        (new[] { "lusail", "losail", "qatar" }, new[] { Red, White }),
        (new[] { "imola", "enzo e dino" }, new[] { Red, White }),
        (new[] { "interlagos", "sao paulo", "carlos pace" }, new[] { Red, White }),
        (new[] { "americas", "cota", "austin" }, new[] { Red, White }),
        (new[] { "silverstone" }, new[] { Red, White }),
        (new[] { "ricard", "castellet" }, new[] { Red, White }),
        (new[] { "catalunya", "barcelona" }, new[] { Red, White }),
        (new[] { "daytona" }, new[] { Red, White }),
        (new[] { "laguna" }, new[] { Red, White }),
        (new[] { "road atlanta", "road_atlanta", "atlanta" }, new[] { Red, White }),
        (new[] { "long beach", "long_beach", "longbeach" }, new[] { Red, White }),
        (new[] { "watkins" }, new[] { Red, White }),
        (new[] { "indianapolis", "indy" }, new[] { Red, White }),
        // Assetto Corsa — circuits Kunos courants
        (new[] { "nurburgring", "nordschleife" }, new[] { Red, White }),
        (new[] { "zandvoort" }, new[] { Red, White }),
        (new[] { "red_bull_ring", "red bull ring", "spielberg" }, new[] { Red, White }),
        (new[] { "brands_hatch", "brands hatch" }, new[] { Red, White }),
        (new[] { "mugello" }, new[] { Red, White }),
        (new[] { "vallelunga" }, new[] { Red, White }),
        (new[] { "suzuka" }, new[] { Red, White }),
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "AviXMirror.kerbs.json");

    static Dictionary<string, string[]>? _table;
    static readonly Dictionary<string, Color[]> Cache = new();

    /// <summary>Couleurs des vibreurs pour ce circuit (rouge/blanc si inconnu).</summary>
    public static Color[] For(string trackName)
    {
        if (Cache.TryGetValue(trackName, out var cached))
            return cached;

        var table = Table();
        string name = Simplify(trackName);
        var match = table
            .Where(e => name.Contains(Simplify(e.Key)))
            .OrderByDescending(e => e.Key.Length)
            .Select(e => e.Value)
            .FirstOrDefault() ?? new[] { Red, White };

        var colors = match.Select(Parse).Where(c => c != Color.Empty).ToArray();
        if (colors.Length == 0)
            colors = new[] { Parse(Red), Parse(White) };
        Cache[trackName] = colors;
        return colors;
    }

    static Dictionary<string, string[]> Table()
    {
        if (_table != null)
            return _table;

        _table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keys, colors) in Defaults)
            foreach (var key in keys)
                _table[key] = colors;

        try
        {
            if (File.Exists(FilePath))
            {
                var user = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath));
                if (user != null)
                    foreach (var (key, colors) in user)
                        _table[key] = colors;
            }
            else
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_table, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Fichier illisible : on garde les valeurs par défaut.
        }
        return _table;
    }

    /// <summary>Minuscules sans accents, « _ » et « - » remplacés par des espaces.</summary>
    static string Simplify(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c is '_' or '-' ? ' ' : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    static Color Parse(string hex)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Color.Empty;
        }
    }
}
