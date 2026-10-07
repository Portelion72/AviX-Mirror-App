using System.IO.MemoryMappedFiles;
using System.Text;

namespace AviXMirror.Radar;

/// <summary>
/// Assetto Corsa Competizione (beta), par sa mémoire partagée officielle : page « graphics »
/// (position de toutes les voitures, état de la session), « physics » (vitesse et cap du joueur) et
/// « static » (circuit, longueur). ACC ne donne ni la vitesse, ni la classe, ni la progression sur le
/// tour des adversaires : vitesses estimées, catégorie GT3.
/// </summary>
public sealed class AccTelemetry : IRadarTelemetry
{
    // Page « graphics » (alignement 4 octets, chaînes en UTF-16).
    const int GStatus = 4;              // 0 éteint, 1 replay, 2 en course, 3 pause
    const int GIsInPit = 160;
    const int GNormalizedPosition = 248;
    const int GActiveCars = 252;
    const int GCarCoordinates = 256;    // float[60][3]
    const int GCarId = 976;             // int[60]
    const int GPlayerCarId = 1216;
    const int GraphicsSize = 1220;
    const int MaxCars = 60;

    // Page « physics ».
    const int PPacketId = 0;
    const int PVelocity = 32;           // float[3], dans le monde
    const int PHeading = 208;
    const int PhysicsSize = 220;

    // Page « static ».
    const int STrack = 134;             // wchar[33]
    const int STrackLength = 520;
    const int StaticSize = 524;

    MemoryMappedFile? _graphicsFile, _physicsFile, _staticFile;
    MemoryMappedViewAccessor? _graphics, _physics, _static;
    readonly byte[] _g = new byte[GraphicsSize];
    readonly byte[] _p = new byte[PhysicsSize];
    readonly byte[] _s = new byte[StaticSize];
    readonly RetryGate _gate = new();
    readonly VelocityEstimator _velocities = new();
    RF2Vec3 _lastForward = WorldMath.Vec(0, 0, 1);

    public string GameName => "Assetto Corsa Competizione";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = "En attente d'Assetto Corsa Competizione…";
            return false;
        }

        try
        {
            _graphics!.ReadArray(0, _g, 0, _g.Length);
            _physics!.ReadArray(0, _p, 0, _p.Length);
            _static!.ReadArray(0, _s, 0, _s.Length);
        }
        catch
        {
            Close();
            status = "Assetto Corsa Competizione : lecture impossible.";
            return false;
        }

        int state = I(_g, GStatus);
        if (state == 0)
        {
            status = "Assetto Corsa Competizione : pas de session en cours.";
            return false;
        }

        int count = Math.Clamp(I(_g, GActiveCars), 0, MaxCars);
        int playerId = I(_g, GPlayerCarId);
        float trackLength = F(_s, STrackLength);
        world = new RadarWorld
        {
            Game = GameName,
            TrackName = W(_s, STrack, 33),
            TrackLength = trackLength,
            Time = I(_p, PPacketId),
            InRealtime = state == 2,
            InvertLateral = settings.BetaInvertLateral,
        };

        // Cap du joueur : direction de sa vitesse (fiable), sinon son cap, sinon la dernière connue.
        var velocity = WorldMath.Vec(F(_p, PVelocity), F(_p, PVelocity + 4), F(_p, PVelocity + 8));
        var up = WorldMath.Vec(0, 1, 0);
        if (WorldMath.Length(velocity) > 3)
        {
            // Direction réelle, pente comprise (montée, descente), lissée pour ne pas trembler sur les vibreurs.
            var direction = WorldMath.Normalize(velocity);
            _lastForward = WorldMath.Normalize(WorldMath.Vec(
                _lastForward.X + (direction.X - _lastForward.X) * 0.3,
                _lastForward.Y + (direction.Y - _lastForward.Y) * 0.3,
                _lastForward.Z + (direction.Z - _lastForward.Z) * 0.3));
        }
        else
        {
            double heading = F(_p, PHeading);
            if (heading != 0)
                _lastForward = WorldMath.Vec(Math.Sin(heading), 0, Math.Cos(heading));
        }

        var ids = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int id = I(_g, GCarId + i * 4);
            int o = GCarCoordinates + i * 12;
            var position = WorldMath.Vec(F(_g, o), F(_g, o + 4), F(_g, o + 8));
            if (position.X == 0 && position.Y == 0 && position.Z == 0)
                continue;
            ids.Add(id);
            bool isPlayer = id == playerId;
            var carVelocity = isPlayer ? velocity : _velocities.Update(id, position);
            var forward = isPlayer || WorldMath.Length(carVelocity) < 3 ? _lastForward : WorldMath.Vec(carVelocity.X, 0, carVelocity.Z);
            var orientation = WorldMath.Orientation(forward, up);
            var vehicle = new RadarVehicle
            {
                Id = id,
                IsPlayer = isPlayer,
                Position = position,
                Velocity = carVelocity,
                Orientation = orientation,
                Left = WorldMath.LeftOf(orientation),
                // Seule la progression du joueur est connue : elle suffit à apprendre le tracé.
                LapDist = isPlayer && trackLength > 0 ? F(_g, GNormalizedPosition) * trackLength : double.NaN,
                Class = "GT3",
                InPits = isPlayer && I(_g, GIsInPit) != 0,
            };
            world.Vehicles.Add(vehicle);
            if (isPlayer)
                world.Player = vehicle;
        }
        _velocities.Keep(ids);

        status = $"Assetto Corsa Competizione connecté (beta) — {world.Vehicles.Count} voitures";
        return true;
    }

    static int I(byte[] b, int offset) => BitConverter.ToInt32(b, offset);
    static float F(byte[] b, int offset) => BitConverter.ToSingle(b, offset);

    static string W(byte[] b, int offset, int chars)
    {
        var text = Encoding.Unicode.GetString(b, offset, chars * 2);
        int end = text.IndexOf('\0');
        return (end < 0 ? text : text[..end]).Trim();
    }

    bool Open()
    {
        if (_graphics != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _graphicsFile = MemoryMappedFile.OpenExisting("Local\\acpmf_graphics", MemoryMappedFileRights.Read);
            _physicsFile = MemoryMappedFile.OpenExisting("Local\\acpmf_physics", MemoryMappedFileRights.Read);
            _staticFile = MemoryMappedFile.OpenExisting("Local\\acpmf_static", MemoryMappedFileRights.Read);
            _graphics = _graphicsFile.CreateViewAccessor(0, GraphicsSize, MemoryMappedFileAccess.Read);
            _physics = _physicsFile.CreateViewAccessor(0, PhysicsSize, MemoryMappedFileAccess.Read);
            _static = _staticFile.CreateViewAccessor(0, StaticSize, MemoryMappedFileAccess.Read);
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
        _graphics?.Dispose(); _physics?.Dispose(); _static?.Dispose();
        _graphicsFile?.Dispose(); _physicsFile?.Dispose(); _staticFile?.Dispose();
        _graphics = _physics = _static = null;
        _graphicsFile = _physicsFile = _staticFile = null;
    }

    public void Dispose() => Close();
}
