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
/// Détermine si le joueur est réellement en train de rouler : jeu lancé, au premier plan, et
/// télémétrie qui avance (horloge du jeu), pour tous les jeux du catalogue.
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

    public GameState Update(Settings s)
    {
        var running = GamesFor(s).Where(g => Games.IsRunning(g, s)).ToList();
        if (running.Count == 0)
            return _last = GameState.NoGame;

        var ids = new HashSet<uint>();
        foreach (var game in running)
            foreach (var name in Games.ProcessesOf(game, s))
                AddProcesses(ids, name);

        bool live = running.Any(g => Live(g, s));

        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foreground);
        if (foreground == _ownProcess)
        {
            // Réglages en cours dans AviX Mirror : on ne bascule pas sur le logo du bureau.
            return _last = live ? GameState.Active : (_last == GameState.Desktop ? GameState.Desktop : GameState.Paused);
        }
        if (!ids.Contains(foreground))
            return _last = GameState.Desktop;
        return _last = live ? GameState.Active : GameState.Paused;
    }

    /// <summary>Au volant : télémétrie lisible, joueur en piste et horloge du jeu qui avance.</summary>
    bool Live(GameInfo game, Settings s)
    {
        var world = _telemetry.For(game).ReadOrNull(s);
        if (world == null || !world.InRealtime || world.Player == null)
            return false;
        if (world.Time != _lastClock)
        {
            _lastClock = world.Time;
            _sinceClock.Restart();
        }
        return _sinceClock.Elapsed.TotalSeconds < FrozenSeconds;
    }

    static void AddProcesses(HashSet<uint> ids, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        foreach (var p in Process.GetProcessesByName(name))
        {
            ids.Add((uint)p.Id);
            p.Dispose();
        }
    }

    public void Dispose() => _telemetry.Dispose();
}
