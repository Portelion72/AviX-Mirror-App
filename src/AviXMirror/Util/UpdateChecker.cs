using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AviXMirror.Util;

/// <summary>Nouvelle version publiée sur la page « Releases » du dépôt officiel.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string Url, string Notes);

/// <summary>
/// Vérifie s'il existe une version plus récente d'AviX Mirror (dernière release GitHub publiée).
/// Aucune donnée n'est envoyée : simple lecture de la page publique des versions.
/// </summary>
public static class UpdateChecker
{
    const string LatestReleaseApi = "https://api.github.com/repos/Portelion72/AviX-Mirror-App/releases/latest";
    public const string ReleasesPage = "https://github.com/Portelion72/AviX-Mirror-App/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AviXMirror", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Version de l'application en cours d'exécution.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>La nouvelle version si elle est plus récente que celle-ci, sinon null (ou en cas d'erreur réseau).</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = json.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                return null;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var latest))
                return null;
            var current = CurrentVersion;
            if (Normalize(latest) <= Normalize(current))
                return null;

            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateInfo(latest, tag, name, url, notes);
        }
        catch
        {
            return null; // hors ligne, GitHub indisponible… on réessaiera plus tard
        }
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        int dash = text.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0)
            text = text[..dash];
        if (!text.Contains('.'))
            text += ".0";
        return Version.TryParse(text, out version!);
    }

    /// <summary>Compare sur 3 chiffres (1.1 = 1.1.0 = 1.1.0.0).</summary>
    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
