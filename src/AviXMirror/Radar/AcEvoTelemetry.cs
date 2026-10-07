using System.IO.MemoryMappedFiles;
using System.Text;

namespace AviXMirror.Radar;

/// <summary>
/// Assetto Corsa EVO (beta), par sa mémoire partagée officielle « Local\acevo_pmf_* »
/// (documentation Kunos ACE_SharedFileOut v1). Page « graphics » : position de toutes les voitures
/// (car_coordinates) et leur identifiant (car_ids, comparé à player_car_id pour trouver le joueur) ;
/// « physics » : vitesse et cap du joueur ; « static » : circuit et longueur.
/// Vitesses des adversaires estimées, catégorie inconnue.
/// </summary>
public sealed class AcEvoTelemetry : IRadarTelemetry
{
    // Page « graphics » (alignement 4 octets ; emplacements calculés d'après la documentation officielle).
    const int GStatus = 4;              // 0 éteint, 1 replay, 2 en course, 3 pause
    const int GPlayerCarId = 24;        // uint64 × 2
    const int GNpos = 1244;             // progression du joueur sur le tour (0..1)
    const int GInPitBox = 3119;
    const int GInPitLane = 3120;
    const int GCarCoordinates = 3124;   // float[60][3]
    const int GActiveCars = 3852;       // uint8
    const int GCarIds = 3940;           // uint64[60][2]
    const int GraphicsSize = 4900;
    const int MaxCars = 60;

    // Page « physics » (début identique à Assetto Corsa).
    const int PPacketId = 0;
    const int PVelocity = 32;
    const int PHeading = 208;
    const int PhysicsSize = 220;

    // Page « static ».
    const int STrack = 136;             // char[33]
    const int STrackLength = 204;
    const int StaticSize = 208;

    MemoryMappedFile? _graphicsFile, _physicsFile, _staticFile;
    MemoryMappedViewAccessor? _graphics, _physics, _static;
    readonly byte[] _g = new byte[GraphicsSize];
    readonly byte[] _p = new byte[PhysicsSize];
    readonly byte[] _s = new byte[StaticSize];
    readonly RetryGate _gate = new();
    readonly VelocityEstimator _velocities = new();
    RF2Vec3 _lastForward = WorldMath.Vec(0, 0, 1);

    public string GameName => "Assetto Corsa EVO";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = "En attente d'Assetto Corsa EVO…";
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
            status = "Assetto Corsa EVO : lecture impossible.";
            return false;
        }

        int state = BitConverter.ToInt32(_g, GStatus);
        if (state == 0)
        {
            status = "Assetto Corsa EVO : pas de session en cours.";
            return false;
        }

        int count = Math.Clamp((int)_g[GActiveCars], 0, MaxCars);
        ulong playerA = BitConverter.ToUInt64(_g, GPlayerCarId), playerB = BitConverter.ToUInt64(_g, GPlayerCarId + 8);
        float trackLength = F(_s, STrackLength);
        world = new RadarWorld
        {
            Game = GameName,
            TrackName = Str(_s, STrack, 33),
            TrackLength = trackLength,
            Time = BitConverter.ToInt32(_p, PPacketId),
            InRealtime = state == 2,
            InvertLateral = settings.BetaInvertLateral,
        };

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

        // Joueur : emplacement dont l'identifiant vaut player_car_id (sinon le premier).
        int playerSlot = 0;
        for (int i = 0; i < count; i++)
        {
            int o = GCarIds + i * 16;
            if (BitConverter.ToUInt64(_g, o) == playerA && BitConverter.ToUInt64(_g, o + 8) == playerB && (playerA | playerB) != 0)
            {
                playerSlot = i;
                break;
            }
        }

        var ids = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int o = GCarCoordinates + i * 12;
            var position = WorldMath.Vec(F(_g, o), F(_g, o + 4), F(_g, o + 8));
            if (position.X == 0 && position.Y == 0 && position.Z == 0)
                continue;
            ids.Add(i);
            bool isPlayer = i == playerSlot;
            var carVelocity = isPlayer ? velocity : _velocities.Update(i, position);
            var forward = isPlayer || WorldMath.Length(carVelocity) < 3 ? _lastForward : WorldMath.Vec(carVelocity.X, 0, carVelocity.Z);
            var orientation = WorldMath.Orientation(forward, up);
            var vehicle = new RadarVehicle
            {
                Id = i,
                IsPlayer = isPlayer,
                Position = position,
                Velocity = carVelocity,
                Orientation = orientation,
                Left = WorldMath.LeftOf(orientation),
                LapDist = isPlayer && trackLength > 0 ? F(_g, GNpos) * trackLength : double.NaN,
                InPits = isPlayer && (_g[GInPitBox] != 0 || _g[GInPitLane] != 0),
            };
            world.Vehicles.Add(vehicle);
            if (isPlayer)
                world.Player = vehicle;
        }
        _velocities.Keep(ids);

        status = $"Assetto Corsa EVO connecté (beta) — {world.Vehicles.Count} voitures";
        return true;
    }

    static float F(byte[] b, int offset) => BitConverter.ToSingle(b, offset);

    static string Str(byte[] b, int offset, int length)
    {
        int end = Array.IndexOf(b, (byte)0, offset, length);
        return Encoding.UTF8.GetString(b, offset, (end < 0 ? offset + length : end) - offset).Trim();
    }

    bool Open()
    {
        if (_graphics != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _graphicsFile = MemoryMappedFile.OpenExisting("Local\\acevo_pmf_graphics", MemoryMappedFileRights.Read);
            _physicsFile = MemoryMappedFile.OpenExisting("Local\\acevo_pmf_physics", MemoryMappedFileRights.Read);
            _staticFile = MemoryMappedFile.OpenExisting("Local\\acevo_pmf_static", MemoryMappedFileRights.Read);
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
