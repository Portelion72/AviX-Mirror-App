using System.Diagnostics;
using AviXMirror.Radar;

namespace AviXMirror.Util;

public enum GameState
{
    /// <summary>Au volant, jeu au premier plan : le rétro fonctionne normalement.</summary>
    Active,
    /// <summary>Jeu en pause ou dans les menus (pas de télémétrie qui avance) : animation AVIX.</summary>
    Paused,
    /// <summary>Jeu lancé mais une autre fenêtre est au premier plan (bureau) : logo AVIX.</summary>
    Desktop,
    /// <summary>Jeu non lancé : animation AVIX.</summary>
    NoGame,
}

/// <summary>
/// Détermine si le joueur est réellement en train de rouler : jeu lancé et télémétrie qui avance (horloge du
/// jeu), même si une autre fenêtre a le focus (plusieurs écrans). Sans télémétrie : actif tant que le jeu
/// n'est pas réduit.
/// </summary>
public sealed class GameActivity : IDisposable
{
    const double FrozenSeconds = 1.2; // horloge de LMU arrêtée depuis ce délai = pause

    readonly TelemetrySet _telemetry = new();
    readonly Stopwatch _sinceClock = Stopwatch.StartNew();
    readonly uint _ownProcess = (uint)Environment.ProcessId;
    double _lastClock = double.NaN;
    GameState _last = GameState.NoGame;

    /// <summary>Jeux concernés par le mode : la caméra AC ne suit qu'Assetto Corsa.</summary>
    static IEnumerable<GameInfo> GamesFor(Settings s) => s.Mode == MirrorMode.CameraAssettoCorsa
        ? new[] { Games.Get(RadarGame.AssettoCorsa) }
        : Games.Candidates(s);

    /// <summary>Voiture du joueur reconnue dans la télémétrie (profils par voiture), null si inconnue.</summary>
    public string? PlayerCar { get; private set; }

    public GameState Update(Settings s)
    {
        var running = GamesFor(s).Where(g => Games.IsRunning(g, s)).ToList();
        if (running.Count == 0)
            return _last = GameState.NoGame;

        var ids = new HashSet<uint>();
        int windows = 0, minimizedWindows = 0;
        foreach (var game in running)
            foreach (var name in Games.ProcessesOf(game, s))
                AddProcesses(ids, name, ref windows, ref minimizedWindows);
        // Réduit seulement si on a trouvé la fenêtre du jeu et qu'elle est réduite.
        bool minimized = windows > 0 && minimizedWindows == windows;

        bool telemetry = false;
        bool live = false;
        foreach (var game in running)
            live |= Live(game, s, ref telemetry);

        // Au volant (horloge du jeu qui avance) : le rétro reste actif quelle que soit la fenêtre au premier plan.
        // Avec plusieurs écrans, on clique souvent ailleurs (SimHub, Discord, AviX Mirror…) sans quitter le jeu.
        if (live)
            return _last = GameState.Active;

        // Pas de télémétrie du tout (plugin désactivé) : impossible de savoir si le jeu est en pause ;
        // le rétro reste actif tant que la fenêtre du jeu n'est pas réduite.
        if (!telemetry)
            return _last = minimized ? GameState.Desktop : GameState.Active;

        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foreground);
        if (foreground == _ownProcess)
        {
            // Réglages en cours dans AviX Mirror : on ne bascule pas sur le logo du bureau.
            return _last = _last == GameState.Desktop ? GameState.Desktop : GameState.Paused;
        }
        // Jeu en pause ou dans les menus : animation ; une autre application au premier plan : logo.
        return _last = ids.Contains(foreground) ? GameState.Paused : GameState.Desktop;
    }

    /// <summary>
    /// Au volant : télémétrie lisible, joueur en piste et horloge du jeu qui avance.
    /// <paramref name="telemetry"/> passe à vrai dès que la télémétrie du jeu est lisible.
    /// </summary>
    bool Live(GameInfo game, Settings s, ref bool telemetry)
    {
        var world = _telemetry.For(game).ReadOrNull(s);
        if (world == null)
            return false;
        telemetry = true;
        if (CarIdentity.Key(game.Game, world.Player) is { } car)
            PlayerCar = car;
        if (!world.InRealtime || world.Player == null)
            return false;
        if (world.Time != _lastClock)
        {
            _lastClock = world.Time;
            _sinceClock.Restart();
        }
        return _sinceClock.Elapsed.TotalSeconds < FrozenSeconds;
    }

    /// <summary>Ajoute les processus du nom donné et compte leurs fenêtres principales (et celles qui sont réduites).</summary>
    static void AddProcesses(HashSet<uint> ids, string name, ref int windows, ref int minimized)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        foreach (var p in Process.GetProcessesByName(name))
        {
            ids.Add((uint)p.Id);
            try
            {
                var window = p.MainWindowHandle;
                if (window != IntPtr.Zero)
                {
                    windows++;
                    if (Native.IsIconic(window))
                        minimized++;
                }
            }
            catch
            {
                // Processus inaccessible : ignoré.
            }
            p.Dispose();
        }
    }

    public void Dispose() => _telemetry.Dispose();
}
