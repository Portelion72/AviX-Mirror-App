namespace AviXMirror.Radar;

/// <summary>
/// Tracé du circuit appris à partir des positions de toutes les voitures.
/// LMU ne fournit pas la géométrie du circuit, mais chaque voiture donne sa position 3D et sa
/// distance parcourue sur le tour : on accumule, tous les 2 m de tour, la position moyenne du centre
/// de la piste et sa demi-largeur. Le tracé est sauvegardé par circuit pour les sessions suivantes.
/// </summary>
public sealed class TrackMap
{
    public const double BinSize = 2.0;
    const int FileVersion = 1;
    const double MaxInterpolation = 80; // m de tour comblés entre deux relevés d'une même voiture

    struct Bin
    {
        // Deux hypothèses pour le centre de piste (position ∓ décalage latéral) : le signe exact
        // de mPathLateral n'étant pas documenté, on garde celle dont la dispersion est la plus faible.
        public double Ax, Ay, Az, Aq;
        public double Bx, By, Bz, Bq;
        public double Edge;
        public int N;
    }

    readonly record struct Sample(double LapDist, double X, double Y, double Z,
        double LeftX, double LeftY, double LeftZ, double Lateral, double Edge);

    readonly Dictionary<int, Sample> _last = new();
    Bin[] _bins = Array.Empty<Bin>();
    string _key = "";
    double _length;
    bool _useA = true;
    int _filled;
    bool _dirty;
    DateTime _lastSave = DateTime.UtcNow;

    public double Length => _length;
    public bool HasData => _filled > 0;

    /// <summary>Part du circuit déjà connue (0 à 1).</summary>
    public double Coverage => _bins.Length == 0 ? 0 : (double)_filled / _bins.Length;

    static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AviXMirror", "circuits");

    /// <summary>Ajoute les positions d'un nouveau relevé de télémétrie.</summary>
    public void Update(RadarWorld world)
    {
        double length = world.TrackLength;
        if (length < 200 || length > 100_000)
            return;

        string key = $"{world.Game}_{world.TrackName}_{Math.Round(length)}";
        if (key != _key)
            SwitchTrack(key, length);

        foreach (var v in world.Vehicles)
        {
            var vel = v.Velocity;
            double speed = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
            if (v.InPits || double.IsNaN(v.LapDist) || speed < 8)
            {
                _last.Remove(v.Id);
                continue;
            }

            var sample = new Sample(Wrap(v.LapDist), v.Position.X, v.Position.Y, v.Position.Z,
                v.Left.X, v.Left.Y, v.Left.Z, v.PathLateral, Math.Abs(v.TrackEdge));

            if (_last.TryGetValue(v.Id, out var prev))
            {
                double delta = Wrap(sample.LapDist - prev.LapDist);
                if (delta > BinSize && delta < MaxInterpolation)
                {
                    // Comble les cases entre deux relevés (LMU envoie ~5 relevés par seconde).
                    int steps = (int)(delta / BinSize);
                    for (int k = 1; k < steps; k++)
                        Add(Lerp(prev, sample, (double)k / steps, prev.LapDist + delta * k / steps));
                }
            }
            Add(sample);
            _last[v.Id] = sample;
        }

        ChooseHypothesis();

        if (_dirty && (DateTime.UtcNow - _lastSave).TotalSeconds > 120)
            Save();
    }

    static Sample Lerp(in Sample a, in Sample b, double t, double lapDist) => new(
        lapDist,
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t,
        a.LeftX + (b.LeftX - a.LeftX) * t, a.LeftY + (b.LeftY - a.LeftY) * t, a.LeftZ + (b.LeftZ - a.LeftZ) * t,
        a.Lateral + (b.Lateral - a.Lateral) * t, a.Edge + (b.Edge - a.Edge) * t);

    double Wrap(double d)
    {
        if (_length <= 0)
            return d;
        d %= _length;
        return d < 0 ? d + _length : d;
    }

    int Index(double lapDist) => Math.Clamp((int)(Wrap(lapDist) / BinSize), 0, _bins.Length - 1);

    void Add(in Sample s)
    {
        if (_bins.Length == 0)
            return;
        ref var b = ref _bins[Index(s.LapDist)];
        double ax = s.X - s.LeftX * s.Lateral, ay = s.Y - s.LeftY * s.Lateral, az = s.Z - s.LeftZ * s.Lateral;
        double bx = s.X + s.LeftX * s.Lateral, by = s.Y + s.LeftY * s.Lateral, bz = s.Z + s.LeftZ * s.Lateral;
        if (b.N == 0)
            _filled++;
        b.Ax += ax; b.Ay += ay; b.Az += az; b.Aq += ax * ax + ay * ay + az * az;
        b.Bx += bx; b.By += by; b.Bz += bz; b.Bq += bx * bx + by * by + bz * bz;
        b.Edge += s.Edge;
        b.N++;
        _dirty = true;
    }

    void ChooseHypothesis()
    {
        double varA = 0, varB = 0;
        foreach (ref readonly var b in _bins.AsSpan())
        {
            if (b.N < 2)
                continue;
            double n = b.N;
            varA += b.Aq / n - (b.Ax * b.Ax + b.Ay * b.Ay + b.Az * b.Az) / (n * n);
            varB += b.Bq / n - (b.Bx * b.Bx + b.By * b.By + b.Bz * b.Bz) / (n * n);
        }
        _useA = varA <= varB;
    }

    bool Center(int index, out double x, out double y, out double z, out double halfWidth)
    {
        ref readonly var b = ref _bins[index];
        if (b.N == 0)
        {
            x = y = z = halfWidth = 0;
            return false;
        }
        double n = b.N;
        if (_useA) { x = b.Ax / n; y = b.Ay / n; z = b.Az / n; }
        else { x = b.Bx / n; y = b.By / n; z = b.Bz / n; }
        double edge = b.Edge / n;
        halfWidth = edge is > 2.5 and < 25 ? edge : 6.0;
        return true;
    }

    /// <summary>
    /// Point du centre de la piste à une distance de tour donnée (lissé sur les cases voisines),
    /// avec la direction de la piste et sa demi-largeur.
    /// </summary>
    public bool TryGetPoint(double lapDist, out RF2Vec3 center, out RF2Vec3 direction, out double halfWidth)
    {
        center = direction = default;
        halfWidth = 0;
        if (_bins.Length == 0)
            return false;

        int i = Index(lapDist);
        double sx = 0, sy = 0, sz = 0, sw = 0;
        int count = 0;
        for (int k = -1; k <= 1; k++)
        {
            if (Center((i + k + _bins.Length) % _bins.Length, out var x, out var y, out var z, out var hw))
            {
                sx += x; sy += y; sz += z; sw += hw;
                count++;
            }
        }
        if (count == 0 || !Center(i, out _, out _, out _, out _))
            return false;
        center = new RF2Vec3 { X = sx / count, Y = sy / count, Z = sz / count };
        halfWidth = sw / count;

        int ahead = (i + 2) % _bins.Length, before = (i - 2 + _bins.Length) % _bins.Length;
        if (Center(ahead, out var ax, out var ay, out var az, out _) &&
            Center(before, out var bx, out var by, out var bz, out _))
            direction = new RF2Vec3 { X = ax - bx, Y = ay - by, Z = az - bz };
        return true;
    }

    void SwitchTrack(string key, double length)
    {
        if (_dirty)
            Save();
        _key = key;
        _length = length;
        _bins = new Bin[(int)Math.Ceiling(length / BinSize)];
        _filled = 0;
        _last.Clear();
        _dirty = false;
        Load();
    }

    string FilePath()
    {
        var name = string.Concat(_key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Folder, name + ".bin");
    }

    void Load()
    {
        try
        {
            var path = FilePath();
            if (!File.Exists(path))
                return;
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadInt32() != FileVersion || reader.ReadInt32() != _bins.Length)
                return;
            for (int i = 0; i < _bins.Length; i++)
            {
                ref var b = ref _bins[i];
                b.Ax = reader.ReadDouble(); b.Ay = reader.ReadDouble(); b.Az = reader.ReadDouble(); b.Aq = reader.ReadDouble();
                b.Bx = reader.ReadDouble(); b.By = reader.ReadDouble(); b.Bz = reader.ReadDouble(); b.Bq = reader.ReadDouble();
                b.Edge = reader.ReadDouble();
                b.N = reader.ReadInt32();
                if (b.N > 0)
                    _filled++;
            }
            ChooseHypothesis();
        }
        catch
        {
            Array.Clear(_bins);
            _filled = 0;
        }
    }

    public void Save()
    {
        if (_bins.Length == 0 || !_dirty)
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            var path = FilePath();
            using (var writer = new BinaryWriter(File.Create(path + ".tmp")))
            {
                writer.Write(FileVersion);
                writer.Write(_bins.Length);
                foreach (var b in _bins)
                {
                    writer.Write(b.Ax); writer.Write(b.Ay); writer.Write(b.Az); writer.Write(b.Aq);
                    writer.Write(b.Bx); writer.Write(b.By); writer.Write(b.Bz); writer.Write(b.Bq);
                    writer.Write(b.Edge);
                    writer.Write(b.N);
                }
            }
            File.Move(path + ".tmp", path, overwrite: true);
            _dirty = false;
            _lastSave = DateTime.UtcNow;
        }
        catch
        {
            // Pas bloquant : le tracé sera réappris.
        }
    }
}
