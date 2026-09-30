using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;

namespace AviXMirror.Radar;

/// <summary>
/// iRacing (beta), par la mémoire partagée officielle du SDK (« Local\IRSDKMemMapFileName »).
/// iRacing ne donne pas la position des autres voitures dans le monde, seulement leur progression
/// sur le tour : le radar est donc « déroulé » sur une ligne droite (distance exacte derrière vous),
/// et les voitures à votre hauteur sont placées à gauche ou à droite d'après le spotter d'iRacing
/// (CarLeftRight). Le tracé du circuit n'est pas dessiné.
/// </summary>
public sealed partial class IRacingTelemetry : IRadarTelemetry
{
    const int MaxCars = 64;
    const int VarHeaderSize = 144;
    const double SideGap = 2.6;       // décalage latéral d'une voiture à côté (m)
    const double AlongsideRange = 6;  // distance max d'une voiture « à côté » (m)

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    readonly RetryGate _gate = new();
    readonly VelocityEstimator _velocities = new();
    readonly Dictionary<string, (int Type, int Offset, int Count)> _vars = new();
    int _varsSignature = -1;
    int _sessionInfoUpdate = -1;
    double _trackLength;
    string _trackName = "";
    readonly Dictionary<int, string> _classes = new();
    byte[] _buffer = Array.Empty<byte>();

    public string GameName => "iRacing";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = "En attente d'iRacing…";
            return false;
        }

        try
        {
            int flags = _view!.ReadInt32(4);
            if ((flags & 1) == 0)
            {
                status = "iRacing : simulateur non connecté.";
                return false;
            }
            ReadHeaders();
            if (!ReadLatestBuffer())
            {
                status = "iRacing : données indisponibles.";
                return false;
            }
        }
        catch
        {
            Close();
            status = "iRacing : lecture impossible.";
            return false;
        }

        int player = Int("PlayerCarIdx");
        if (player < 0 || player >= MaxCars || _trackLength <= 0)
        {
            status = "iRacing : pas de session en cours.";
            return false;
        }

        double L = _trackLength;
        float playerPct = Float("CarIdxLapDistPct", player);
        int playerLaps = Int("CarIdxLapCompleted", player);
        double playerDistance = (Math.Max(0, playerLaps) + playerPct) * L;
        double speed = Float("Speed");

        world = new RadarWorld
        {
            Game = GameName,
            TrackName = _trackName,
            TrackLength = 0, // monde « déroulé » : pas de tracé à apprendre
            Time = Double("SessionTime"),
            InRealtime = Bool("IsOnTrack") && !Bool("IsReplayPlaying"),
            InvertLateral = settings.BetaInvertLateral,
        };

        // Monde « déroulé » : tout le monde avance vers -z ; x = gauche.
        var orientation = new[] { WorldMath.Vec(1, 0, 0), WorldMath.Vec(0, 1, 0), WorldMath.Vec(0, 0, 1) };
        var alongside = new List<(int Car, double Gap)>();
        var cars = new List<(int Car, double Gap)>();
        for (int i = 0; i < MaxCars; i++)
        {
            if (i == player)
                continue;
            int surface = Int("CarIdxTrackSurface", i);
            if (surface < 0) // pas en piste (-1 = absent)
                continue;
            float pct = Float("CarIdxLapDistPct", i);
            if (pct < 0)
                continue;
            // Écart le long de la piste, ramené entre -L/2 et +L/2 (positif = derrière).
            double gap = (playerPct - pct) * L;
            gap -= Math.Round(gap / L) * L;
            cars.Add((i, gap));
            if (Math.Abs(gap) < AlongsideRange)
                alongside.Add((i, gap));
        }

        // Voitures à côté : placées du côté indiqué par le spotter d'iRacing.
        var lateral = new Dictionary<int, double>();
        int leftRight = Int("CarLeftRight");
        var near = alongside.OrderBy(c => Math.Abs(c.Gap)).Select(c => c.Car).ToList();
        void Place(int index, double side)
        {
            if (index < near.Count)
                lateral[near[index]] = side * SideGap;
        }
        switch (leftRight)
        {
            case 2: Place(0, 1); break;                         // voiture à gauche
            case 3: Place(0, -1); break;                        // voiture à droite
            case 4: Place(0, 1); Place(1, -1); break;           // des deux côtés
            case 5: Place(0, 1); Place(1, 1); break;            // 2 voitures à gauche
            case 6: Place(0, -1); Place(1, -1); break;          // 2 voitures à droite
        }

        var playerVehicle = new RadarVehicle
        {
            Id = player,
            IsPlayer = true,
            Position = WorldMath.Vec(0, 0, -playerDistance),
            Velocity = WorldMath.Vec(0, 0, -speed),
            Orientation = orientation,
            Left = WorldMath.Vec(1, 0, 0),
            Class = _classes.GetValueOrDefault(player, ""),
            InPits = Bool("CarIdxOnPitRoad", player),
        };
        world.Vehicles.Add(playerVehicle);
        world.Player = playerVehicle;

        var ids = new List<int> { player };
        foreach (var (car, gap) in cars)
        {
            var position = WorldMath.Vec(lateral.GetValueOrDefault(car, 0), 0, -playerDistance + gap);
            ids.Add(car);
            world.Vehicles.Add(new RadarVehicle
            {
                Id = car,
                Position = position,
                Velocity = _velocities.Update(car, position),
                Orientation = orientation,
                Left = WorldMath.Vec(1, 0, 0),
                Class = _classes.GetValueOrDefault(car, ""),
                InPits = Bool("CarIdxOnPitRoad", car),
                Place = Int("CarIdxPosition", car),
            });
        }
        _velocities.Keep(ids);

        status = $"iRacing connecté (beta, radar simplifié) — {world.Vehicles.Count} voitures";
        return true;
    }

    // ---------- SDK ----------

    void ReadHeaders()
    {
        int numVars = _view!.ReadInt32(24);
        int varHeaderOffset = _view.ReadInt32(28);
        int signature = numVars * 31 + varHeaderOffset;
        if (signature != _varsSignature)
        {
            _vars.Clear();
            var header = new byte[VarHeaderSize];
            for (int i = 0; i < numVars; i++)
            {
                _view.ReadArray(varHeaderOffset + i * VarHeaderSize, header, 0, VarHeaderSize);
                int type = BitConverter.ToInt32(header, 0);
                int offset = BitConverter.ToInt32(header, 4);
                int count = BitConverter.ToInt32(header, 8);
                int end = Array.IndexOf(header, (byte)0, 16, 32);
                string name = Encoding.ASCII.GetString(header, 16, (end < 0 ? 48 : end) - 16);
                _vars[name] = (type, offset, count);
            }
            _varsSignature = signature;
        }

        // Informations de session (YAML) : longueur du circuit, classes des voitures.
        int sessionUpdate = _view.ReadInt32(12);
        if (sessionUpdate != _sessionInfoUpdate)
        {
            int length = _view.ReadInt32(16);
            int offset = _view.ReadInt32(20);
            var yaml = new byte[Math.Clamp(length, 0, 4 * 1024 * 1024)];
            _view.ReadArray(offset, yaml, 0, yaml.Length);
            ParseSessionInfo(Encoding.Latin1.GetString(yaml));
            _sessionInfoUpdate = sessionUpdate;
        }
    }

    void ParseSessionInfo(string yaml)
    {
        var length = TrackLengthRegex().Match(yaml);
        if (length.Success && double.TryParse(length.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var km))
            _trackLength = km * (length.Groups[2].Value == "mi" ? 1609.344 : 1000);
        var name = TrackNameRegex().Match(yaml);
        if (name.Success)
            _trackName = name.Groups[1].Value.Trim();

        _classes.Clear();
        foreach (var block in yaml.Split("- CarIdx:").Skip(1))
        {
            int end = block.IndexOfAny(new[] { '\r', '\n' });
            if (!int.TryParse((end < 0 ? block : block[..end]).Trim(), out int idx))
                continue;
            var cls = CarClassRegex().Match(block);
            var car = CarNameRegex().Match(block);
            _classes[idx] = ((cls.Success ? cls.Groups[1].Value : "") + " " + (car.Success ? car.Groups[1].Value : "")).Trim();
        }
    }

    [GeneratedRegex(@"TrackLength:\s*([\d.]+)\s*(km|mi)")]
    private static partial Regex TrackLengthRegex();

    [GeneratedRegex(@"TrackDisplayName:\s*(.+)")]
    private static partial Regex TrackNameRegex();

    [GeneratedRegex(@"CarClassShortName:\s*(.+)")]
    private static partial Regex CarClassRegex();

    [GeneratedRegex(@"CarScreenNameShort:\s*(.+)")]
    private static partial Regex CarNameRegex();

    /// <summary>Copie le tampon de variables le plus récent (sur 4), en vérifiant qu'il n'a pas changé pendant la copie.</summary>
    bool ReadLatestBuffer()
    {
        int numBuf = Math.Clamp(_view!.ReadInt32(32), 1, 4);
        int bufLen = _view.ReadInt32(36);
        if (bufLen <= 0 || bufLen > 4 * 1024 * 1024)
            return false;
        if (_buffer.Length != bufLen)
            _buffer = new byte[bufLen];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int best = 0, bestTick = int.MinValue;
            for (int i = 0; i < numBuf; i++)
            {
                int tick = _view.ReadInt32(48 + i * 16);
                if (tick > bestTick)
                {
                    bestTick = tick;
                    best = i;
                }
            }
            int bufOffset = _view.ReadInt32(48 + best * 16 + 4);
            _view.ReadArray(bufOffset, _buffer, 0, bufLen);
            if (_view.ReadInt32(48 + best * 16) == bestTick)
                return true;
        }
        return false;
    }

    int Int(string name, int index = 0) =>
        _vars.TryGetValue(name, out var v) && index < v.Count ? BitConverter.ToInt32(_buffer, v.Offset + index * 4) : -1;

    float Float(string name, int index = 0) =>
        _vars.TryGetValue(name, out var v) && index < v.Count ? BitConverter.ToSingle(_buffer, v.Offset + index * 4) : -1;

    double Double(string name) =>
        _vars.TryGetValue(name, out var v) ? BitConverter.ToDouble(_buffer, v.Offset) : 0;

    bool Bool(string name, int index = 0) =>
        _vars.TryGetValue(name, out var v) && index < v.Count && _buffer[v.Offset + index] != 0;

    bool Open()
    {
        if (_view != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _file = MemoryMappedFile.OpenExisting("Local\\IRSDKMemMapFileName", MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _varsSignature = -1;
            _sessionInfoUpdate = -1;
            return true;
        }
        catch
        {
            Close();
            _gate.Failed();
            return false;
        }
    }

    void Close()
    {
        _view?.Dispose();
        _file?.Dispose();
        _view = null;
        _file = null;
    }

    public void Dispose() => Close();
}
