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
/// télémétrie qui avance (horloge de LMU, compteur de l'app Assetto Corsa).
/// </summary>
public sealed class GameActivity : IDisposable
{
    const double FrozenSeconds = 1.2; // horloge de LMU arrêtée depuis ce délai = pause

    readonly Rf2Telemetry _rf2 = new();
    readonly AcTelemetry _ac = new();
    readonly Stopwatch _sinceClock = Stopwatch.StartNew();
    readonly uint _ownProcess = (uint)Environment.ProcessId;
    double _lastClock = double.NaN;
    GameState _last = GameState.NoGame;

    public GameState Update(Settings s)
    {
        bool lmu = s.Mode == MirrorMode.Capture || (s.Mode == MirrorMode.Radar && s.RadarGame != RadarGame.AssettoCorsa);
        bool ac = s.Mode == MirrorMode.CameraAssettoCorsa || (s.Mode == MirrorMode.Radar && s.RadarGame != RadarGame.LeMansUltimate);

        var games = new HashSet<uint>();
        if (lmu)
            AddProcesses(games, s.GameProcessName);
        if (ac)
            AddProcesses(games, s.AcProcessName);
        if (games.Count == 0)
            return _last = GameState.NoGame;

        bool live = (lmu && LmuLive(s)) || (ac && _ac.TryRead(s, out var acWorld, out _) && acWorld != null);

        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foreground);
        if (foreground == _ownProcess)
        {
            // Réglages en cours dans AviX Mirror : on ne bascule pas sur le logo du bureau.
            return _last = live ? GameState.Active : (_last == GameState.Desktop ? GameState.Desktop : GameState.Paused);
        }
        if (!games.Contains(foreground))
            return _last = GameState.Desktop;
        return _last = live ? GameState.Active : GameState.Paused;
    }

    bool LmuLive(Settings s)
    {
        if (!_rf2.TryRead(s, out var world, out _) || world == null || !world.InRealtime || world.Player == null)
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

    public void Dispose()
    {
        _rf2.Dispose();
        _ac.Dispose();
    }
}
