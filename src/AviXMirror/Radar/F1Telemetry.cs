using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AviXMirror.Radar;

/// <summary>
/// F1 23 / F1 24 / F1 25 (beta), par la télémétrie UDP officielle du jeu (Réglages → Télémétrie :
/// UDP activée, port 20777 par défaut, format 2023 ou plus récent). Le paquet « Motion » donne la
/// position, la vitesse et la direction des 22 voitures.
/// </summary>
public sealed class F1Telemetry : IRadarTelemetry
{
    const double StaleSeconds = 1.2; // plus de paquet depuis ce délai = jeu en pause ou fermé

    bool _registered;

    public string GameName => "F1";

    public bool TryRead(Settings settings, out RadarWorld? world, out string status)
    {
        world = null;
        if (!_registered)
        {
            F1UdpReceiver.Acquire();
            _registered = true;
        }
        F1UdpReceiver.Ensure(settings.F1UdpPort);
        var motion = F1UdpReceiver.Latest;
        if (motion == null)
        {
            status = $"En attente de F1 (télémétrie UDP sur le port {settings.F1UdpPort})…";
            return false;
        }
        if (motion.Age > StaleSeconds)
        {
            status = "F1 : pas de nouvelles données (jeu en pause ou télémétrie coupée).";
            return false;
        }

        world = new RadarWorld
        {
            Game = GameName,
            TrackName = "",
            TrackLength = 0,
            Time = motion.SessionTime,
            InRealtime = true,
            InvertLateral = settings.BetaInvertLateral,
        };

        for (int i = 0; i < motion.Cars.Length; i++)
        {
            var car = motion.Cars[i];
            // Voiture absente : position nulle.
            if (car.Position.X == 0 && car.Position.Y == 0 && car.Position.Z == 0)
                continue;
            // F1 donne directement l'axe « droite » : gauche = -droite (le repère du jeu est indirect).
            var orientation = WorldMath.Orientation(car.Forward, WorldMath.Vec(-car.Right.X, -car.Right.Y, -car.Right.Z), car.Up);
            var vehicle = new RadarVehicle
            {
                Id = i,
                IsPlayer = i == motion.PlayerIndex,
                Position = car.Position,
                Velocity = car.Velocity,
                Orientation = orientation,
                Left = WorldMath.LeftOf(orientation),
                Class = "Formula",
            };
            world.Vehicles.Add(vehicle);
            if (vehicle.IsPlayer)
                world.Player = vehicle;
        }

        status = $"F1 connecté (beta, format {motion.Format}) — {world.Vehicles.Count} voitures";
        return true;
    }

    public void Dispose()
    {
        if (_registered)
            F1UdpReceiver.Release();
        _registered = false;
    }
}

/// <summary>Dernier paquet « Motion » reçu.</summary>
public sealed class F1Motion
{
    public record struct Car(RF2Vec3 Position, RF2Vec3 Velocity, RF2Vec3 Forward, RF2Vec3 Right, RF2Vec3 Up);

    public int Format;
    public int PlayerIndex;
    public double SessionTime;
    public Car[] Cars = Array.Empty<Car>();
    public long Received;

    public double Age => (Stopwatch.GetTimestamp() - Received) / (double)Stopwatch.Frequency;
}

/// <summary>
/// Réception UDP partagée par toute l'application (un seul programme peut écouter un port) :
/// ouverte tant qu'au moins une source F1 est utilisée.
/// </summary>
public static class F1UdpReceiver
{
    const int CarCount = 22;
    const int CarMotionSize = 60;

    static readonly object Lock = new();
    static UdpClient? _client;
    static Thread? _thread;
    static int _port;
    static int _users;
    static volatile F1Motion? _latest;
    static readonly RetryGate Gate = new();

    public static F1Motion? Latest => _latest;

    /// <summary>Une source F1 de plus utilise la réception.</summary>
    public static void Acquire()
    {
        lock (Lock)
            _users++;
    }

    /// <summary>Ouvre l'écoute sur le port demandé (si elle n'est pas déjà ouverte).</summary>
    public static void Ensure(int port)
    {
        lock (Lock)
        {
            if (_client != null && _port == port)
                return;
            if (!Gate.Ready)
                return;
            StopLocked();
            try
            {
                var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _client = client;
                _port = port;
                _thread = new Thread(() => Receive(client)) { IsBackground = true, Name = "F1 UDP" };
                _thread.Start();
            }
            catch
            {
                _client = null; // port occupé (autre logiciel de télémétrie) : on réessaiera
                Gate.Failed();
            }
        }
    }

    public static void Release()
    {
        lock (Lock)
        {
            if (--_users <= 0)
            {
                _users = 0;
                StopLocked();
            }
        }
    }

    static void StopLocked()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        _latest = null;
    }

    static void Receive(UdpClient client)
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            byte[] data;
            try
            {
                data = client.Receive(ref remote);
            }
            catch
            {
                return; // socket fermé
            }
            try
            {
                var motion = Parse(data);
                if (motion != null)
                    _latest = motion;
            }
            catch
            {
                // Paquet inattendu : ignoré.
            }
        }
    }

    /// <summary>Décode un paquet « Motion » (identifiant 0), formats 2022 à 2025.</summary>
    public static F1Motion? Parse(byte[] d)
    {
        if (d.Length < 24)
            return null;
        int format = BitConverter.ToUInt16(d, 0);
        bool modern = format >= 2023;                  // en-tête de 29 octets (année du jeu, compteur global)
        int header = modern ? 29 : 24;
        int packetId = d[modern ? 6 : 5];
        if (packetId != 0 || d.Length < header + CarCount * CarMotionSize)
            return null;
        float sessionTime = BitConverter.ToSingle(d, modern ? 15 : 14);
        int player = d[modern ? 27 : 22];

        var cars = new F1Motion.Car[CarCount];
        for (int i = 0; i < CarCount; i++)
        {
            int o = header + i * CarMotionSize;
            var position = WorldMath.Vec(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));
            var velocity = WorldMath.Vec(BitConverter.ToSingle(d, o + 12), BitConverter.ToSingle(d, o + 16), BitConverter.ToSingle(d, o + 20));
            var forward = WorldMath.Vec(Norm(d, o + 24), Norm(d, o + 26), Norm(d, o + 28));
            var right = WorldMath.Vec(Norm(d, o + 30), Norm(d, o + 32), Norm(d, o + 34));
            // Haut = perpendiculaire à l'avant et à la droite, orienté vers le haut du monde (y).
            var up = WorldMath.Normalize(WorldMath.Cross(right, forward));
            if (up.Y < 0)
                up = WorldMath.Vec(-up.X, -up.Y, -up.Z);
            if (WorldMath.Length(forward) < 0.5)
                up = WorldMath.Vec(0, 1, 0);
            cars[i] = new F1Motion.Car(position, velocity, forward, right, up);
        }

        return new F1Motion
        {
            Format = format,
            PlayerIndex = player,
            SessionTime = sessionTime,
            Cars = cars,
            Received = Stopwatch.GetTimestamp(),
        };
    }

    static double Norm(byte[] d, int offset) => BitConverter.ToInt16(d, offset) / 32767.0;
}
