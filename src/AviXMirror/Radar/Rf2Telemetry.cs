namespace AviXMirror.Radar;

/// <summary>Le Mans Ultimate (et rFactor 2) via le plugin « rF2 Shared Memory Map ».</summary>
public sealed class Rf2Telemetry : IRadarTelemetry
{
    readonly RF2ScoringReader _reader = new();
    uint _lastVersion;
    RadarWorld? _lastWorld;
    string _lastStatus = "";

    public string GameName => "Le Mans Ultimate";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        // Rien de nouveau depuis la dernière lecture : on renvoie l'état déjà décodé (lecture à chaque image sans coût).
        if (_lastWorld != null && _reader.TryPeekVersion(out var version) && version == _lastVersion)
        {
            world = _lastWorld;
            status = _lastStatus;
            return true;
        }

        if (!_reader.TryRead(out var scoring))
        {
            status = "En attente de LMU (plugin rF2 Shared Memory Map)…";
            return false;
        }

        var info = scoring.ScoringInfo;
        if (info.NumVehicles < 0 || info.NumVehicles > RF2Scoring.MaxVehicles || scoring.Vehicles == null)
        {
            status = "LMU : données de télémétrie invalides.";
            return false;
        }

        world = new RadarWorld
        {
            Game = GameName,
            TrackName = RF2ScoringReader.DecodeString(info.TrackName),
            TrackLength = info.LapDist,
            Time = info.CurrentET,
            InvertLateral = settings.RadarInvertLateral,
        };

        for (int i = 0; i < info.NumVehicles; i++)
        {
            ref readonly var v = ref scoring.Vehicles[i];
            if (v.Ori == null || v.InGarageStall != 0)
                continue;
            var vehicle = new RadarVehicle
            {
                Id = v.ID,
                IsPlayer = v.IsPlayer != 0,
                Position = v.Pos,
                Velocity = new RF2Vec3
                {
                    X = Dot(v.Ori[0], v.LocalVel),
                    Y = Dot(v.Ori[1], v.LocalVel),
                    Z = Dot(v.Ori[2], v.LocalVel),
                },
                Left = new RF2Vec3 { X = v.Ori[0].X, Y = v.Ori[1].X, Z = v.Ori[2].X },
                Orientation = v.Ori,
                LocalRotation = v.LocalRot,
                LapDist = v.LapDist,
                PathLateral = v.PathLateral,
                TrackEdge = v.TrackEdge,
                Class = RF2ScoringReader.DecodeString(v.VehicleClass),
                Name = RF2ScoringReader.DecodeString(v.DriverName),
                Place = v.Place,
                Headlights = v.Headlights != 0,
                InPits = v.InPits != 0,
            };
            world.Vehicles.Add(vehicle);
            if (vehicle.IsPlayer)
                world.Player = vehicle;
        }

        status = $"LMU connecté — {world.Vehicles.Count} voitures";
        _lastVersion = scoring.VersionUpdateEnd;
        _lastWorld = world;
        _lastStatus = status;
        return true;
    }

    static double Dot(in RF2Vec3 a, in RF2Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public void Dispose() => _reader.Dispose();
}
