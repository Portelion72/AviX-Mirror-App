using System.IO.MemoryMappedFiles;
using System.Text;

namespace AviXMirror.Radar;

/// <summary>
/// Assetto Corsa via la petite app Lua « AviX Mirror » pour Custom Shaders Patch (installée avec
/// Content Manager). La mémoire partagée officielle d'AC ne donne que la voiture du joueur : l'app
/// exporte la position, la direction, la vitesse, la progression sur le tour et le modèle de toutes
/// les voitures dans la mémoire partagée « AviXMirror.AC.v1 ».
/// </summary>
public sealed class AcTelemetry : IRadarTelemetry
{
    public const string MapName = "AviXMirror.AC.v1";
    public const int MaxCars = 64;
    const int Version = 2;

    // Disposition identique à integrations/AssettoCorsa/AviXMirror/AviXMirror.lua (champs de 4 octets).
    const int OffVersion = 0, OffPacket = 4, OffCount = 8, OffPlayer = 12, OffTrackLength = 16, OffTrack = 20;
    const int TrackChars = 64;
    const int OffArrays = OffTrack + TrackChars;          // 15 tableaux de flottants/entiers de 64 éléments
    const int FloatArrays = 13, IntArrays = 2;
    const int OffModel = OffArrays + (FloatArrays + IntArrays) * MaxCars * 4;
    const int ModelChars = 48, DriverChars = 32;
    const int OffDriver = OffModel + MaxCars * ModelChars;
    public const int Size = 9104; // OffDriver + MaxCars * DriverChars + bloc caméra (voir AcCamera)

    enum F { PosX, PosY, PosZ, LookX, LookY, LookZ, UpX, UpY, UpZ, VelX, VelY, VelZ, Spline, RacePosition, Flags }

    const int FlagPitLane = 1, FlagHeadlights = 2, FlagConnected = 4;

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    readonly byte[] _buffer = new byte[Size];
    int _lastPacket = -1;
    readonly System.Diagnostics.Stopwatch _sincePacket = System.Diagnostics.Stopwatch.StartNew();

    public string GameName => "Assetto Corsa";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!Open())
        {
            status = "En attente d'Assetto Corsa (app Lua « AviX Mirror » pour CSP)…";
            return false;
        }

        _view!.ReadArray(0, _buffer, 0, _buffer.Length);
        if (I(OffVersion) != Version)
        {
            status = "Assetto Corsa : version de l'app Lua « AviX Mirror » incompatible, réinstallez-la.";
            return false;
        }

        int packet = I(OffPacket);
        if (packet != _lastPacket)
        {
            _lastPacket = packet;
            _sincePacket.Restart();
        }
        else if (_sincePacket.Elapsed.TotalSeconds > 1.5)
        {
            // AC fermé ou en pause : la mémoire reste mais n'évolue plus.
            status = "Assetto Corsa : pas de nouvelles données (jeu en pause ou fermé).";
            Close();
            return false;
        }

        int count = Math.Clamp(I(OffCount), 0, MaxCars);
        int player = I(OffPlayer);
        world = new RadarWorld
        {
            Game = GameName,
            TrackName = Str(OffTrack, TrackChars),
            TrackLength = BitConverter.ToSingle(_buffer, OffTrackLength),
            Time = packet,
            InvertLateral = settings.AcInvertLateral,
        };

        for (int i = 0; i < count; i++)
        {
            int flags = Int(F.Flags, i);
            if ((flags & FlagConnected) == 0)
                continue;

            var look = Vec(F.LookX, i);
            var up = Vec(F.UpX, i);
            // Repère rF2 : x = gauche, y = haut, z = arrière (repère direct : gauche = avant ^ haut).
            var left = Normalize(Cross(look, up));
            var back = new RF2Vec3 { X = -look.X, Y = -look.Y, Z = -look.Z };
            float spline = Flt(F.Spline, i);

            var vehicle = new RadarVehicle
            {
                Id = i,
                IsPlayer = i == player,
                Position = Vec(F.PosX, i),
                Velocity = Vec(F.VelX, i),
                Left = left,
                Orientation = new[]
                {
                    new RF2Vec3 { X = left.X, Y = up.X, Z = back.X },
                    new RF2Vec3 { X = left.Y, Y = up.Y, Z = back.Y },
                    new RF2Vec3 { X = left.Z, Y = up.Z, Z = back.Z },
                },
                LapDist = spline is >= 0 and <= 1 && world.TrackLength > 0 ? spline * world.TrackLength : double.NaN,
                Class = Str(OffModel + i * ModelChars, ModelChars),
                Name = Str(OffDriver + i * DriverChars, DriverChars),
                Place = Int(F.RacePosition, i),
                Headlights = (flags & FlagHeadlights) != 0,
                InPits = (flags & FlagPitLane) != 0,
            };
            world.Vehicles.Add(vehicle);
            if (vehicle.IsPlayer)
                world.Player = vehicle;
        }

        status = $"Assetto Corsa connecté — {world.Vehicles.Count} voitures";
        return true;
    }

    int I(int offset) => BitConverter.ToInt32(_buffer, offset);
    float Flt(F array, int i) => BitConverter.ToSingle(_buffer, OffArrays + ((int)array * MaxCars + i) * 4);
    int Int(F array, int i) => BitConverter.ToInt32(_buffer, OffArrays + ((int)array * MaxCars + i) * 4);
    RF2Vec3 Vec(F first, int i) => new() { X = Flt(first, i), Y = Flt(first + 1, i), Z = Flt(first + 2, i) };

    string Str(int offset, int length)
    {
        int end = Array.IndexOf(_buffer, (byte)0, offset, length);
        return Encoding.UTF8.GetString(_buffer, offset, (end < 0 ? offset + length : end) - offset).Trim();
    }

    static RF2Vec3 Cross(in RF2Vec3 a, in RF2Vec3 b) => new()
    {
        X = a.Y * b.Z - a.Z * b.Y,
        Y = a.Z * b.X - a.X * b.Z,
        Z = a.X * b.Y - a.Y * b.X,
    };

    static RF2Vec3 Normalize(in RF2Vec3 v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-6 ? v : new RF2Vec3 { X = v.X / len, Y = v.Y / len, Z = v.Z / len };
    }

    bool Open()
    {
        if (_view != null)
            return true;
        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
            _lastPacket = -1;
            _sincePacket.Restart();
            return true;
        }
        catch
        {
            Close();
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
