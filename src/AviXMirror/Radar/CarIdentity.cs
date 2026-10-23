using System.Globalization;
using System.Text.RegularExpressions;

namespace AviXMirror.Radar;

/// <summary>
/// Identifiant stable de la voiture du joueur, pour les profils de réglages par voiture (la position du rétro
/// virtuel change d'un cockpit à l'autre) : modèle reconnu (LMU), dossier de la voiture (Assetto Corsa)…
/// Null si le jeu ne donne pas le modèle de la voiture.
/// </summary>
public static class CarIdentity
{
    public static string? Key(RadarGame game, RadarVehicle? player)
    {
        if (player == null)
            return null;
        switch (game)
        {
            case RadarGame.LeMansUltimate:
            case RadarGame.RFactor2:
            {
                // Modèle reconnu par les faces avant (« ferrari_499p »…), sinon le nom sans numéro ni équipe.
                var kind = CarClasses.Classify(player.Class);
                var front = string.IsNullOrEmpty(player.Model) ? null : CarFronts.Find(RadarSource.KindName(kind), player.Model, player.Class);
                if (front != null)
                    return front.Id;
                var name = Regex.Replace(player.Model ?? "", @"#\s*\d+.*$", "").Trim();
                name = Regex.Replace(name, @"\s+\d+$", "").Trim();
                return name.Length > 0 ? Slug(name) : null;
            }
            case RadarGame.AssettoCorsa:
                return string.IsNullOrWhiteSpace(player.Class) ? null : Slug(player.Class);
            default:
                return null; // ACC, iRacing, F1… : pas de modèle fiable dans la télémétrie lue
        }
    }

    static string Slug(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

    /// <summary>Nom lisible d'un identifiant (« ferrari_499p » → « Ferrari 499P »).</summary>
    public static string Display(string key)
    {
        var words = key.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w =>
            w.Any(char.IsDigit) ? w.ToUpperInvariant() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w));
        return string.Join(" ", words);
    }
}
