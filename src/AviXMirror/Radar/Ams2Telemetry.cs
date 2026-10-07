using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace AviXMirror.Radar;

/// <summary>
/// Automobilista 2 et Project CARS 2 (beta), par la mémoire partagée « $pcars2$ » (dans AMS2 :
/// Options → Système → Mémoire partagée = « Project CARS 2 »). Position et progression sur le tour de
/// toutes les voitures ; vitesse et orientation du joueur. Vitesses des adversaires estimées.
/// </summary>
public sealed class Ams2Telemetry : IRadarTelemetry
{
    const int OGameState = 8;               // 2 = en course, 3 = pause…
    const int OViewedParticipant = 20;
    const int ONumParticipants = 24;
    const int OParticipants = 28;           // ParticipantInfo[64], 100 octets chacun
    const int ParticipantSize = 100;
    const int PActive = 0, PName = 1, PPosition = 68, PLapDistance = 80;
    const int MaxParticipants = 64;
    const int OCarClassName = 6508;         // char[64] (voiture du joueur)
    const int OTrackLocation = 6576;        // char[64]
    const int OTrackLength = 6704;
    const int OPitMode = 6808;
    const int OOrientation = 6904;          // float[3] : tangage, lacet, roulis (radians)
    const int OWorldVelocity = 6928;        // float[3]
    const int Size = 6940;

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    readonly byte[] _b = new byte[Size];
    readonly RetryGate _gate = new();
    readonly VelocityEstimator _velocities = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    RF2Vec3 _lastForward = WorldMath.Vec(0, 0, 1);

    public string GameName => "Automobilista 2";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = "En attente d'Automobilista 2 / Project CARS 2 (mémoire partagée « Project CARS 2 »)…";
            return false;
        }
        try
        {
            _view!.ReadArray(0, _b, 0, _b.Length);
        }
        catch
        {
            Close();
            status = "Automobilista 2 : lecture impossible.";
            return false;
        }

        int state = I(OGameState);
        int count = I(ONumParticipants);
        int viewed = I(OViewedParticipant);
        if (count <= 0 || count > MaxParticipants || state is 0 or 1 or 7)
        {
            status = "Automobilista 2 : pas de session en cours.";
            return false;
        }

        float trackLength = F(OTrackLength);
        world = new RadarWorld
        {
            Game = GameName,
            TrackName = Str(OTrackLocation, 64),
            TrackLength = trackLength,
            // Horloge locale : les positions sont rafraîchies à chaque image du jeu.
            Time = _clock.Elapsed.TotalSeconds,
            InRealtime = state == 2,
            InvertLateral = settings.BetaInvertLateral,
        };

        var up = WorldMath.Vec(0, 1, 0);
        var velocity = WorldMath.Vec(F(OWorldVelocity), F(OWorldVelocity + 4), F(OWorldVelocity + 8));
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
            double yaw = F(OOrientation + 4);
            if (yaw != 0)
                _lastForward = WorldMath.Vec(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        }

        var ids = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int o = OParticipants + i * ParticipantSize;
            if (_b[o + PActive] == 0)
                continue;
            var position = WorldMath.Vec(F(o + PPosition), F(o + PPosition + 4), F(o + PPosition + 8));
            bool isPlayer = i == viewed;
            var carVelocity = isPlayer ? velocity : _velocities.Update(i, position);
            var forward = isPlayer || WorldMath.Length(carVelocity) < 3 ? _lastForward : WorldMath.Vec(carVelocity.X, 0, carVelocity.Z);
            var orientation = WorldMath.Orientation(forward, up);
            float lapDistance = F(o + PLapDistance);
            ids.Add(i);
            var vehicle = new RadarVehicle
            {
                Id = i,
                IsPlayer = isPlayer,
                Position = position,
                Velocity = carVelocity,
                Orientation = orientation,
                Left = WorldMath.LeftOf(orientation),
                LapDist = lapDistance >= 0 && trackLength > 0 && lapDistance <= trackLength ? lapDistance : double.NaN,
                Class = isPlayer ? Str(OCarClassName, 64) : "",
                Name = Str(o + PName, 64),
                InPits = isPlayer && I(OPitMode) is 1 or 2 or 3,
            };
            world.Vehicles.Add(vehicle);
            if (isPlayer)
                world.Player = vehicle;
        }
        _velocities.Keep(ids);

        status = $"Automobilista 2 / Project CARS 2 connecté (beta) — {world.Vehicles.Count} voitures";
        return true;
    }

    int I(int offset) => BitConverter.ToInt32(_b, offset);
    float F(int offset) => BitConverter.ToSingle(_b, offset);

    string Str(int offset, int length)
    {
        int end = Array.IndexOf(_b, (byte)0, offset, length);
        return Encoding.UTF8.GetString(_b, offset, (end < 0 ? offset + length : end) - offset).Trim();
    }

    bool Open()
    {
        if (_view != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _file = MemoryMappedFile.OpenExisting("$pcars2$", MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
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
