namespace AviXMirror.Radar;

/// <summary>
/// Mouvement fluide à partir de relevés peu fréquents (LMU : ~5 par seconde).
/// Entre deux relevés, chaque voiture est prolongée avec sa vitesse, et l'orientation du joueur avec
/// sa vitesse de rotation. À chaque nouveau relevé, l'écart avec la prédiction n'est pas appliqué
/// d'un coup : il se résorbe en douceur (≈ 0,1 s), ce qui supprime les saccades.
/// </summary>
public sealed class MotionSmoother
{
    const double Tau = 0.10;            // constante de temps de la correction (s)
    const double MaxExtrapolation = 0.5; // au-delà, on arrête de prolonger (jeu en pause…)
    const double TeleportDistance = 15;  // écart au-delà duquel on ne lisse pas (sortie des stands, reset)

    sealed class State
    {
        public RF2Vec3 Position, Velocity, Error;
        public double SampleTime, ErrorTime;
        public RF2Vec3[]? Orientation, OrientationFrom;
        public RF2Vec3 Rotation;
    }

    readonly Dictionary<int, State> _states = new();

    // Sens de la vitesse de rotation (+1 ou -1) : la convention de LMU n'étant pas documentée, on
    // compare en continu les deux hypothèses avec les relevés réels et on garde la meilleure.
    double _errPlus, _errMinus;
    public int RotationSign => _errMinus < _errPlus ? -1 : 1;

    public void OnSample(RadarWorld world, double now)
    {
        var seen = new HashSet<int>();
        foreach (var v in world.Vehicles)
        {
            seen.Add(v.Id);
            _states.TryGetValue(v.Id, out var old);
            var st = new State
            {
                Position = v.Position,
                Velocity = v.Velocity,
                SampleTime = now,
                ErrorTime = now,
                Orientation = v.Orientation,
                Rotation = v.LocalRotation,
            };

            if (old != null)
            {
                var shown = Position(old, now);
                var err = Sub(shown, v.Position);
                if (Length(err) < TeleportDistance)
                    st.Error = err;

                if (v.Orientation != null && old.Orientation != null)
                {
                    st.OrientationFrom = Orientation(old, now);
                    if (v.IsPlayer)
                        LearnRotationSign(old, v.Orientation, now);
                }
            }
            _states[v.Id] = st;
        }

        foreach (var id in _states.Keys.Where(id => !seen.Contains(id)).ToList())
            _states.Remove(id);
    }

    public RF2Vec3 Position(RadarVehicle v, double now) =>
        _states.TryGetValue(v.Id, out var st) ? Position(st, now) : v.Position;

    public RF2Vec3[]? Orientation(RadarVehicle v, double now) =>
        _states.TryGetValue(v.Id, out var st) ? Orientation(st, now) : v.Orientation;

    static RF2Vec3 Position(State st, double now)
    {
        double dt = Math.Clamp(now - st.SampleTime, 0, MaxExtrapolation);
        double k = Math.Exp(-(now - st.ErrorTime) / Tau);
        return new RF2Vec3
        {
            X = st.Position.X + st.Velocity.X * dt + st.Error.X * k,
            Y = st.Position.Y + st.Velocity.Y * dt + st.Error.Y * k,
            Z = st.Position.Z + st.Velocity.Z * dt + st.Error.Z * k,
        };
    }

    RF2Vec3[]? Orientation(State st, double now)
    {
        if (st.Orientation == null)
            return null;
        double dt = Math.Clamp(now - st.SampleTime, 0, MaxExtrapolation);
        var predicted = Rotate(st.Orientation, st.Rotation, dt * RotationSign);
        if (st.OrientationFrom == null)
            return predicted;
        double k = Math.Exp(-(now - st.ErrorTime) / Tau);
        return k < 0.01 ? predicted : Orthonormalize(Lerp(predicted, st.OrientationFrom, k));
    }

    void LearnRotationSign(State old, RF2Vec3[] actual, double now)
    {
        double dt = Math.Clamp(now - old.SampleTime, 0, MaxExtrapolation);
        if (dt <= 0 || old.Orientation == null || Length(old.Rotation) < 0.05)
            return;
        double plus = Distance(Rotate(old.Orientation, old.Rotation, dt), actual);
        double minus = Distance(Rotate(old.Orientation, old.Rotation, -dt), actual);
        _errPlus = _errPlus * 0.95 + plus;
        _errMinus = _errMinus * 0.95 + minus;
    }

    /// <summary>
    /// Applique une rotation exprimée dans le repère local (rad/s × dt) à une orientation au format rF2
    /// (lignes de la matrice local -> monde) : M' = M · exp([ω]× dt), approximée puis réorthonormalisée.
    /// </summary>
    static RF2Vec3[] Rotate(RF2Vec3[] m, RF2Vec3 w, double dt)
    {
        double ax = w.X * dt, ay = w.Y * dt, az = w.Z * dt;
        // R ≈ I + [a]× (petits angles, dt ≤ 0,5 s)
        double[,] r =
        {
            { 1, -az, ay },
            { az, 1, -ax },
            { -ay, ax, 1 },
        };
        var result = new RF2Vec3[3];
        for (int i = 0; i < 3; i++)
        {
            double[] row = { m[i].X, m[i].Y, m[i].Z };
            result[i] = new RF2Vec3
            {
                X = row[0] * r[0, 0] + row[1] * r[1, 0] + row[2] * r[2, 0],
                Y = row[0] * r[0, 1] + row[1] * r[1, 1] + row[2] * r[2, 1],
                Z = row[0] * r[0, 2] + row[1] * r[1, 2] + row[2] * r[2, 2],
            };
        }
        return Orthonormalize(result);
    }

    /// <summary>Réorthonormalise les colonnes (axes locaux gauche, haut, arrière exprimés dans le monde).</summary>
    static RF2Vec3[] Orthonormalize(RF2Vec3[] m)
    {
        var c0 = new RF2Vec3 { X = m[0].X, Y = m[1].X, Z = m[2].X };
        var c1 = new RF2Vec3 { X = m[0].Y, Y = m[1].Y, Z = m[2].Y };
        c0 = Normalize(c0);
        c1 = Normalize(Sub(c1, Scale(c0, Dot(c0, c1))));
        // Troisième axe : produit vectoriel, avec le même sens que celui fourni par le jeu.
        var original = new RF2Vec3 { X = m[0].Z, Y = m[1].Z, Z = m[2].Z };
        var c2 = Cross(c0, c1);
        if (Dot(c2, original) < 0)
            c2 = Scale(c2, -1);
        return new[]
        {
            new RF2Vec3 { X = c0.X, Y = c1.X, Z = c2.X },
            new RF2Vec3 { X = c0.Y, Y = c1.Y, Z = c2.Y },
            new RF2Vec3 { X = c0.Z, Y = c1.Z, Z = c2.Z },
        };
    }

    static RF2Vec3[] Lerp(RF2Vec3[] a, RF2Vec3[] b, double k) =>
        a.Select((row, i) => new RF2Vec3
        {
            X = row.X + (b[i].X - row.X) * k,
            Y = row.Y + (b[i].Y - row.Y) * k,
            Z = row.Z + (b[i].Z - row.Z) * k,
        }).ToArray();

    static double Distance(RF2Vec3[] a, RF2Vec3[] b) =>
        Enumerable.Range(0, 3).Sum(i => Length(Sub(a[i], b[i])));

    static RF2Vec3 Sub(RF2Vec3 a, RF2Vec3 b) => new() { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
    static RF2Vec3 Scale(RF2Vec3 a, double k) => new() { X = a.X * k, Y = a.Y * k, Z = a.Z * k };
    static double Dot(RF2Vec3 a, RF2Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static double Length(RF2Vec3 a) => Math.Sqrt(Dot(a, a));
    static RF2Vec3 Normalize(RF2Vec3 a) { double l = Length(a); return l < 1e-9 ? a : Scale(a, 1 / l); }
    static RF2Vec3 Cross(RF2Vec3 a, RF2Vec3 b) => new()
    {
        X = a.Y * b.Z - a.Z * b.Y,
        Y = a.Z * b.X - a.X * b.Z,
        Z = a.X * b.Y - a.Y * b.X,
    };
}
