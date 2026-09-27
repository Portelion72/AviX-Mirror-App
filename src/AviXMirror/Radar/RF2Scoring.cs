using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace AviXMirror.Radar;

// Structures du plugin "rF2 Shared Memory Map" (TheIronWolf), utilisé par SimHub et CrewChief
// avec rFactor 2 et Le Mans Ultimate. Alignement 4 octets, comme le plugin (#pragma pack(4)).

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RF2Vec3
{
    public double X, Y, Z;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
public struct RF2ScoringInfo
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] TrackName;
    public int Session;
    public double CurrentET;
    public double EndET;
    public int MaxLaps;
    public double LapDist;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Pointer1;
    public int NumVehicles;
    public byte GamePhase;
    public sbyte YellowFlagState;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public sbyte[] SectorFlag;
    public byte StartLight;
    public byte NumRedLights;
    public byte InRealtime;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PlayerName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] PlrFileName;
    public double DarkCloud;
    public double Raining;
    public double AmbientTemp;
    public double TrackTemp;
    public RF2Vec3 Wind;
    public double MinPathWetness;
    public double MaxPathWetness;
    public byte GameMode;
    public byte IsPasswordProtected;
    public ushort ServerPort;
    public uint ServerPublicIP;
    public int MaxPlayers;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] ServerName;
    public float StartET;
    public double AvgPathWetness;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 200)] public byte[] Expansion;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Pointer2;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
public struct RF2VehicleScoring
{
    public int ID;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] DriverName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] VehicleName;
    public short TotalLaps;
    public sbyte Sector;
    public sbyte FinishStatus;
    public double LapDist;
    public double PathLateral;
    public double TrackEdge;
    public double BestSector1;
    public double BestSector2;
    public double BestLapTime;
    public double LastSector1;
    public double LastSector2;
    public double LastLapTime;
    public double CurSector1;
    public double CurSector2;
    public short NumPitstops;
    public short NumPenalties;
    public byte IsPlayer;
    public sbyte Control;
    public byte InPits;
    public byte Place;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] VehicleClass;
    public double TimeBehindNext;
    public int LapsBehindNext;
    public double TimeBehindLeader;
    public int LapsBehindLeader;
    public double LapStartET;
    public RF2Vec3 Pos;
    public RF2Vec3 LocalVel;
    public RF2Vec3 LocalAccel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public RF2Vec3[] Ori;
    public RF2Vec3 LocalRot;
    public RF2Vec3 LocalRotAccel;
    public byte Headlights;
    public byte PitState;
    public byte ServerScored;
    public byte IndividualPhase;
    public int Qualification;
    public double TimeIntoLap;
    public double EstimatedLapTime;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)] public byte[] PitGroup;
    public byte Flag;
    public byte UnderYellow;
    public byte CountLapFlag;
    public byte InGarageStall;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] UpgradePack;
    public float PitLapDist;
    public float BestLapSector1;
    public float BestLapSector2;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] Expansion;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
public struct RF2Scoring
{
    public const int MaxVehicles = 128;

    public uint VersionUpdateBegin;
    public uint VersionUpdateEnd;
    public int BytesUpdatedHint;
    public RF2ScoringInfo ScoringInfo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxVehicles)] public RF2VehicleScoring[] Vehicles;
}

/// <summary>Lecture de la mémoire partagée "$rFactor2SMMP_Scoring$".</summary>
public sealed class RF2ScoringReader : IDisposable
{
    const string MapName = "$rFactor2SMMP_Scoring$";

    static readonly int Size = Marshal.SizeOf<RF2Scoring>();

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    readonly byte[] _buffer = new byte[Size];

    public bool Connected => _view != null;

    bool TryOpen()
    {
        if (_view != null)
            return true;
        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
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

    /// <summary>Compteur de mise à jour du plugin (change à chaque nouveau relevé).</summary>
    public bool TryPeekVersion(out uint version)
    {
        version = 0;
        if (!TryOpen())
            return false;
        try
        {
            version = _view!.ReadUInt32(4); // mVersionUpdateEnd
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    /// <summary>Lit une copie cohérente des données. Retourne faux si LMU/le plugin n'est pas actif.</summary>
    public bool TryRead(out RF2Scoring scoring)
    {
        scoring = default;
        if (!TryOpen())
            return false;

        try
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                _view!.ReadArray(0, _buffer, 0, _buffer.Length);
                uint begin = BitConverter.ToUInt32(_buffer, 0);
                uint end = BitConverter.ToUInt32(_buffer, 4);
                if (begin != end)
                {
                    Thread.Yield();
                    continue;
                }

                var handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                try
                {
                    scoring = Marshal.PtrToStructure<RF2Scoring>(handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
                return true;
            }
        }
        catch
        {
            Close();
        }
        return false;
    }

    public static string DecodeString(byte[]? bytes)
    {
        if (bytes == null)
            return "";
        int len = Array.IndexOf(bytes, (byte)0);
        if (len < 0)
            len = bytes.Length;
        return Encoding.Latin1.GetString(bytes, 0, len).Trim();
    }

    public void Dispose() => Close();
}
