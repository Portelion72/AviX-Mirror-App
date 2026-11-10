using System.Globalization;
using System.IO.Ports;

namespace AviXMirror.Output;

/// <summary>
/// Installe le programme LEDs AviX sur un Arduino Nano, sans Arduino IDE : fichier Intel HEX intégré
/// (compilé par la CI), envoyé au chargeur de démarrage du Nano par le protocole STK500 v1 — 115200 bauds
/// (Optiboot, « nouveau » chargeur) puis 57600 bauds (ancien chargeur, clones CH340), comme avrdude.
/// </summary>
public static class ArduinoFlasher
{
    const int PageSize = 128; // ATmega328P : pages de 64 mots
    const byte StkOk = 0x10, StkInSync = 0x14, CrcEop = 0x20;

    /// <summary>Programme intégré à l'exe (null si la CI ne l'a pas compilé).</summary>
    public static byte[]? Firmware()
    {
        using var stream = typeof(ArduinoFlasher).Assembly.GetManifestResourceStream("Arduino.AviXMirrorLeds.hex");
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return ParseHex(reader.ReadToEnd());
    }

    /// <summary>Image mémoire d'un fichier Intel HEX (adresses absolues, octets libres à 0xFF).</summary>
    public static byte[] ParseHex(string text)
    {
        var image = new byte[32 * 1024];
        Array.Fill(image, (byte)0xFF);
        int top = 0, baseAddress = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 11 || line[0] != ':')
                continue;
            byte Byte(int i) => byte.Parse(line.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int count = Byte(0);
            int address = (Byte(1) << 8) | Byte(2);
            int type = Byte(3);
            byte sum = 0;
            for (int i = 0; i < count + 5; i++)
                sum += Byte(i);
            if (sum != 0)
                throw new InvalidDataException("fichier HEX abîmé (somme de contrôle)");
            switch (type)
            {
                case 0:
                    for (int i = 0; i < count; i++)
                    {
                        int at = baseAddress + address + i;
                        if (at >= image.Length)
                            throw new InvalidDataException("programme trop gros pour un Nano");
                        image[at] = Byte(4 + i);
                        top = Math.Max(top, at + 1);
                    }
                    break;
                case 1:
                    return image[..top];
                case 2:
                    baseAddress = ((Byte(4) << 8) | Byte(5)) << 4;
                    break;
                case 4:
                    baseAddress = ((Byte(4) << 8) | Byte(5)) << 16;
                    break;
            }
        }
        return image[..top];
    }

    /// <summary>Programme le Nano sur <paramref name="portName"/>. Lève une exception claire en cas d'échec.</summary>
    public static void Flash(string portName, byte[] program, Action<string>? progress = null)
    {
        Exception? last = null;
        foreach (int baud in new[] { 115200, 57600 })
        {
            try
            {
                progress?.Invoke(baud == 115200 ? "Connexion au Nano…" : "Connexion au Nano (ancien chargeur)…");
                using var port = new SerialPort(portName, baud) { ReadTimeout = 500, WriteTimeout = 1000 };
                port.Open();
                Reset(port);
                Sync(port);
                Command(port, 0x50); // entrée en programmation
                try
                {
                    for (int at = 0; at < program.Length; at += PageSize)
                    {
                        progress?.Invoke($"Écriture… {at * 100 / program.Length} %");
                        var page = Page(program, at);
                        LoadAddress(port, at);
                        Command(port, 0x64, new byte[] { 0, PageSize, (byte)'F' }.Concat(page).ToArray());
                    }
                    for (int at = 0; at < program.Length; at += PageSize)
                    {
                        progress?.Invoke($"Vérification… {at * 100 / program.Length} %");
                        LoadAddress(port, at);
                        var read = Command(port, 0x74, new byte[] { 0, PageSize, (byte)'F' }, PageSize);
                        if (!read.AsSpan().SequenceEqual(Page(program, at)))
                            throw new IOException($"vérification ratée à l'adresse 0x{at:X4}");
                    }
                }
                finally
                {
                    try { Command(port, 0x51); } catch { } // sortie : le programme démarre
                }
                progress?.Invoke("Terminé.");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                throw new IOException($"{portName} est occupé (SimHub, Arduino IDE ou AviX Mirror ouvert ailleurs ?).");
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new IOException("le Nano ne répond pas à son chargeur de démarrage (" + last?.Message + ").", last);
    }

    static byte[] Page(byte[] program, int at)
    {
        var page = new byte[PageSize];
        Array.Fill(page, (byte)0xFF);
        Array.Copy(program, at, page, 0, Math.Min(PageSize, program.Length - at));
        return page;
    }

    /// <summary>Redémarre le Nano (ligne DTR, comme avrdude) pour lancer son chargeur de démarrage.</summary>
    static void Reset(SerialPort port)
    {
        port.DtrEnable = false;
        port.RtsEnable = false;
        Thread.Sleep(250);
        port.DtrEnable = true;
        port.RtsEnable = true;
        Thread.Sleep(50);
        port.DiscardInBuffer();
    }

    static void Sync(SerialPort port)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                port.DiscardInBuffer();
                Command(port, 0x30);
                return;
            }
            catch (Exception) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
        }
    }

    static void LoadAddress(SerialPort port, int byteAddress)
    {
        int word = byteAddress / 2;
        Command(port, 0x55, new[] { (byte)(word & 0xFF), (byte)(word >> 8) });
    }

    /// <summary>Envoie une commande STK500 et lit la réponse (14 [données] 10).</summary>
    static byte[] Command(SerialPort port, byte command, byte[]? args = null, int replyLength = 0)
    {
        var frame = new byte[2 + (args?.Length ?? 0)];
        frame[0] = command;
        args?.CopyTo(frame, 1);
        frame[^1] = CrcEop;
        port.Write(frame, 0, frame.Length);
        if (port.ReadByte() != StkInSync)
            throw new IOException("pas de synchronisation avec le Nano");
        var reply = new byte[replyLength];
        for (int i = 0; i < replyLength; i++)
            reply[i] = (byte)port.ReadByte();
        if (port.ReadByte() != StkOk)
            throw new IOException("commande refusée par le Nano");
        return reply;
    }
}
