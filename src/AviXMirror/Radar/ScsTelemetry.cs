using System.IO.MemoryMappedFiles;

namespace AviXMirror.Radar;

/// <summary>
/// Euro Truck Simulator 2 et American Truck Simulator (beta), par le plugin AviX Mirror du SDK de
/// télémétrie officiel SCS (integrations/TruckSim, installé dans « bin\win_x64\plugins »), qui écrit la
/// mémoire partagée « Local\AviXMirror.SCS.v1 » (88 octets, disposition identique au plugin).
/// Le SDK ne donne que le camion du joueur : pas de trafic (ni radar, ni flèches, ni alertes de LEDs),
/// mais la pause, la vitesse et les clignotants.
/// </summary>
public sealed class ScsTelemetry : IRadarTelemetry
{
    const string MapName = "Local\\AviXMirror.SCS.v1";
    const int Size = 88;
    const uint Magic = 0x53585641; // 'AVXS'

    // Disposition (voir AviXScsShared dans AviXMirrorScs.cpp).
    const int OVersion = 4, OFrame = 8, OPaused = 12, OX = 16, OY = 24, OZ = 32;
    const int OHeading = 40, OPitch = 44, OSpeed = 52, OLeftBlinker = 68, ORightBlinker = 72, OGame = 76;

    readonly uint _game; // 1 = ETS2, 2 = ATS
    readonly string _name;
    readonly byte[] _b = new byte[Size];
    readonly RetryGate _gate = new();
    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;

    public ScsTelemetry(uint game, string name)
    {
        _game = game;
        _name = name;
    }

    public string GameName => _name;

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = $"{_name} : plugin AviX Mirror non chargé (bouton « Installer le plugin ETS2 / ATS », puis relancez le jeu).";
            return false;
        }
        try
        {
            _view!.ReadArray(0, _b, 0, Size);
        }
        catch
        {
            Close();
            status = $"{_name} : lecture impossible.";
            return false;
        }
        if (BitConverter.ToUInt32(_b, 0) != Magic || BitConverter.ToUInt32(_b, OVersion) != 1)
        {
            status = $"{_name} : plugin AviX Mirror d'une autre version, réinstallez-le.";
            return false;
        }
        if (BitConverter.ToUInt32(_b, OGame) != _game)
        {
            status = $"{_name} : pas lancé.";
            return false;
        }

        bool paused = BitConverter.ToUInt32(_b, OPaused) != 0;
        double heading = BitConverter.ToSingle(_b, OHeading) * 2 * Math.PI;
        double pitch = BitConverter.ToSingle(_b, OPitch) * 2 * Math.PI;
        // Repère SCS : x = est, y = haut, z = sud ; cap 0 = nord (−z), sens trigonométrique vu de dessus.
        var forward = WorldMath.Vec(-Math.Sin(heading) * Math.Cos(pitch), Math.Sin(pitch), -Math.Cos(heading) * Math.Cos(pitch));
        var up = WorldMath.Vec(0, 1, 0);
        var left = WorldMath.Normalize(WorldMath.Cross(up, forward));
        up = WorldMath.Normalize(WorldMath.Cross(forward, left));
        var orientation = WorldMath.Orientation(forward, left, up);
        double speed = BitConverter.ToSingle(_b, OSpeed);

        var player = new RadarVehicle
        {
            Id = 0,
            IsPlayer = true,
            Position = WorldMath.Vec(BitConverter.ToDouble(_b, OX), BitConverter.ToDouble(_b, OY), BitConverter.ToDouble(_b, OZ)),
            Velocity = WorldMath.Vec(forward.X * speed, forward.Y * speed, forward.Z * speed),
            Orientation = orientation,
            Left = left,
            Class = "Truck",
        };
        world = new RadarWorld
        {
            Game = _name,
            Time = BitConverter.ToUInt32(_b, OFrame),
            InRealtime = !paused,
            Player = player,
            LeftBlinker = BitConverter.ToUInt32(_b, OLeftBlinker) != 0,
            RightBlinker = BitConverter.ToUInt32(_b, ORightBlinker) != 0,
        };
        world.Vehicles.Add(player);
        status = $"{_name} connecté (beta) — {(paused ? "pause" : $"{Math.Abs(speed) * 3.6:0} km/h")}";
        return true;
    }

    bool Open()
    {
        if (_view != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
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
