using System.Diagnostics;

namespace AviXMirror.Radar;

/// <summary>Outils communs aux télémétries : repère au format rF2, vitesses estimées.</summary>
public static class WorldMath
{
    public static RF2Vec3 Vec(double x, double y, double z) => new() { X = x, Y = y, Z = z };

    public static RF2Vec3 Cross(in RF2Vec3 a, in RF2Vec3 b) => new()
    {
        X = a.Y * b.Z - a.Z * b.Y,
        Y = a.Z * b.X - a.X * b.Z,
        Z = a.X * b.Y - a.Y * b.X,
    };

    public static double Length(in RF2Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    public static RF2Vec3 Normalize(in RF2Vec3 v)
    {
        double len = Length(v);
        return len < 1e-6 ? v : new RF2Vec3 { X = v.X / len, Y = v.Y / len, Z = v.Z / len };
    }

    /// <summary>
    /// Orientation au format rF2 (lignes de la matrice local -> monde ; local : x = gauche, y = haut,
    /// z = arrière) à partir de la direction avant et du haut dans le monde (même convention que l'app AC).
    /// </summary>
    public static RF2Vec3[] Orientation(RF2Vec3 forward, RF2Vec3 up)
    {
        forward = Normalize(forward);
        var left = Normalize(Cross(forward, up));
        up = Normalize(Cross(left, forward));
        var back = new RF2Vec3 { X = -forward.X, Y = -forward.Y, Z = -forward.Z };
        return new[]
        {
            new RF2Vec3 { X = left.X, Y = up.X, Z = back.X },
            new RF2Vec3 { X = left.Y, Y = up.Y, Z = back.Y },
            new RF2Vec3 { X = left.Z, Y = up.Z, Z = back.Z },
        };
    }

    /// <summary>Orientation au format rF2 à partir des axes avant, gauche et haut déjà connus (dans le monde).</summary>
    public static RF2Vec3[] Orientation(RF2Vec3 forward, RF2Vec3 left, RF2Vec3 up)
    {
        forward = Normalize(forward);
        left = Normalize(left);
        up = Normalize(up);
        var back = new RF2Vec3 { X = -forward.X, Y = -forward.Y, Z = -forward.Z };
        return new[]
        {
            new RF2Vec3 { X = left.X, Y = up.X, Z = back.X },
            new RF2Vec3 { X = left.Y, Y = up.Y, Z = back.Y },
            new RF2Vec3 { X = left.Z, Y = up.Z, Z = back.Z },
        };
    }

    /// <summary>Vecteur « gauche » (dans le monde) d'une orientation au format rF2.</summary>
    public static RF2Vec3 LeftOf(RF2Vec3[] o) => new() { X = o[0].X, Y = o[1].X, Z = o[2].X };
}

/// <summary>
/// Vitesse de chaque voiture estimée à partir de ses positions successives (pour les jeux qui ne
/// donnent pas la vitesse des adversaires), lissée pour absorber le bruit.
/// </summary>
public sealed class VelocityEstimator
{
    readonly Dictionary<int, (RF2Vec3 Position, double Time, RF2Vec3 Velocity)> _last = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public RF2Vec3 Update(int id, RF2Vec3 position)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (!_last.TryGetValue(id, out var prev))
        {
            _last[id] = (position, now, default);
            return default;
        }
        double dt = now - prev.Time;
        if (dt < 0.005)
            return prev.Velocity; // même relevé
        var raw = new RF2Vec3
        {
            X = (position.X - prev.Position.X) / dt,
            Y = (position.Y - prev.Position.Y) / dt,
            Z = (position.Z - prev.Position.Z) / dt,
        };
        // Téléportation (sortie des stands, reset) : on repart de zéro.
        if (WorldMath.Length(raw) > 150 || dt > 1)
            raw = default;
        double k = Math.Clamp(dt / 0.15, 0.1, 1); // lissage ~0,15 s
        var v = new RF2Vec3
        {
            X = prev.Velocity.X + (raw.X - prev.Velocity.X) * k,
            Y = prev.Velocity.Y + (raw.Y - prev.Velocity.Y) * k,
            Z = prev.Velocity.Z + (raw.Z - prev.Velocity.Z) * k,
        };
        _last[id] = (position, now, v);
        return v;
    }

    /// <summary>Oublie les voitures qui ne sont plus présentes.</summary>
    public void Keep(IEnumerable<int> ids)
    {
        var keep = ids.ToHashSet();
        foreach (var id in _last.Keys.Where(id => !keep.Contains(id)).ToList())
            _last.Remove(id);
    }
}

/// <summary>Limite les tentatives d'ouverture d'une mémoire partagée absente (jeu non lancé) à une par seconde.</summary>
public sealed class RetryGate
{
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _next;

    public bool Ready => _clock.Elapsed.TotalSeconds >= _next;

    public void Failed() => _next = _clock.Elapsed.TotalSeconds + 1;
}
