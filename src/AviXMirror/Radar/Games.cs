using System.Diagnostics;

namespace AviXMirror.Radar;

/// <summary>Un jeu pris en charge : ses processus et sa source de télémétrie.</summary>
public sealed record GameInfo(RadarGame Game, string Name, string[] Processes, bool Beta, Func<IRadarTelemetry> CreateTelemetry);

/// <summary>
/// Catalogue des jeux. En mode « Auto », seuls les jeux dont le processus tourne sont lus
/// (évite de confondre deux jeux qui partagent une mémoire, comme LMU et rFactor 2, ou AC et ACC).
/// </summary>
public static class Games
{
    public static readonly GameInfo[] All =
    {
        new(RadarGame.LeMansUltimate, "Le Mans Ultimate", new[] { "Le Mans Ultimate" }, false, () => new Rf2Telemetry()),
        new(RadarGame.AssettoCorsa, "Assetto Corsa", new[] { "acs" }, false, () => new AcTelemetry()),
        new(RadarGame.RFactor2, "rFactor 2", new[] { "rFactor2" }, true, () => new Rf2Telemetry("rFactor 2", "rFactor 2")),
        new(RadarGame.AssettoCorsaCompetizione, "Assetto Corsa Competizione", new[] { "AC2-Win64-Shipping" }, true, () => new AccTelemetry()),
        new(RadarGame.AssettoCorsaEvo, "Assetto Corsa EVO", new[] { "AssettoCorsaEVO" }, true, () => new AcEvoTelemetry()),
        new(RadarGame.Automobilista2, "Automobilista 2 / Project CARS 2", new[] { "AMS2AVX", "AMS2", "pCARS2AVX", "pCARS2", "pCARS2Gld" }, true, () => new Ams2Telemetry()),
        new(RadarGame.IRacing, "iRacing", new[] { "iRacingSim64DX11", "iRacingSim64" }, true, () => new IRacingTelemetry()),
        new(RadarGame.F1, "F1 23 / 24 / 25", new[] { "F1_23", "F1_24", "F1_25" }, true, () => new F1Telemetry()),
    };

    public static GameInfo Get(RadarGame game) => All.First(g => g.Game == game);

    /// <summary>Processus d'un jeu (LMU : nom réglable dans le fichier de réglages).</summary>
    public static IEnumerable<string> ProcessesOf(GameInfo game, Settings s) =>
        game.Game == RadarGame.LeMansUltimate && !string.IsNullOrWhiteSpace(s.GameProcessName)
            ? game.Processes.Append(s.GameProcessName).Distinct()
            : game.Processes;

    /// <summary>Jeux candidats pour le réglage « Jeu » : celui choisi, ou tous en mode Auto.</summary>
    public static IEnumerable<GameInfo> Candidates(Settings s) =>
        s.RadarGame == RadarGame.Auto ? All : All.Where(g => g.Game == s.RadarGame);

    static readonly object Lock = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double _checkedAt = double.NegativeInfinity;
    static HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Noms des processus en cours d'exécution (relus toutes les 2 s au plus).</summary>
    static HashSet<string> RunningProcesses()
    {
        lock (Lock)
        {
            if (Clock.Elapsed.TotalSeconds - _checkedAt > 2)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try { names.Add(p.ProcessName); } catch { }
                    p.Dispose();
                }
                _running = names;
                _checkedAt = Clock.Elapsed.TotalSeconds;
            }
            return _running;
        }
    }

    public static bool IsRunning(GameInfo game, Settings s)
    {
        var running = RunningProcesses();
        return ProcessesOf(game, s).Any(p => running.Contains(p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p));
    }

    /// <summary>
    /// Jeux à lire maintenant : le jeu choisi, ou en mode Auto ceux qui tournent
    /// (tous si aucun ne tourne, pour afficher un message d'attente).
    /// </summary>
    public static IEnumerable<GameInfo> Active(Settings s)
    {
        if (s.RadarGame != RadarGame.Auto)
            return All.Where(g => g.Game == s.RadarGame);
        var running = All.Where(g => IsRunning(g, s)).ToList();
        return running.Count > 0 ? running : All.Where(g => !g.Beta);
    }
}

/// <summary>
/// Sources de télémétrie d'un composant (radar, ATH, spotter…) : une par jeu, créée à la demande,
/// lues dans l'ordre des jeux actifs.
/// </summary>
public sealed class TelemetrySet : IDisposable
{
    readonly Dictionary<RadarGame, IRadarTelemetry> _sources = new();
    readonly object _lock = new();

    public IRadarTelemetry For(GameInfo game)
    {
        lock (_lock)
        {
            if (!_sources.TryGetValue(game.Game, out var source))
                _sources[game.Game] = source = game.CreateTelemetry();
            return source;
        }
    }

    /// <summary>Premier état de course lisible parmi les jeux actifs (ou restreints par <paramref name="filter"/>).</summary>
    public RadarWorld? Read(Settings s, out string status, Func<GameInfo, bool>? filter = null)
    {
        var messages = new List<string>();
        foreach (var game in Games.Active(s))
        {
            if (filter != null && !filter(game))
                continue;
            if (For(game).TryRead(s, out var world, out var message) && world != null)
            {
                status = message;
                return world;
            }
            messages.Add(message);
        }
        status = string.Join("\n", messages);
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var source in _sources.Values)
                source.Dispose();
            _sources.Clear();
        }
    }
}
