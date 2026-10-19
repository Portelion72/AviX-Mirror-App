using System.Runtime.InteropServices;

namespace AviXMirror.Output;

/// <summary>Un écran VoCore branché : numéro de série (s'il en a un) et port USB.</summary>
public sealed record VoCoreDeviceInfo(string Serial, string Port)
{
    /// <summary>Identifiant enregistré dans les réglages : numéro de série, sinon port USB.</summary>
    public string Key => Serial.Length > 0 ? Serial : "USB " + Port;

    public bool Matches(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        (string.Equals(key, Key, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(key, "USB " + Port, StringComparison.OrdinalIgnoreCase) ||
         (Serial.Length > 0 && string.Equals(key, Serial, StringComparison.OrdinalIgnoreCase)));

    public string Label => Serial.Length > 0 ? $"n° {Serial} (port USB {Port})" : $"port USB {Port}";
}

/// <summary>
/// Reconnaissance des écrans VoCore (comme SimHub) : liste des écrans branchés, chacun reconnu par son
/// numéro de série USB ou, à défaut, par le port USB où il est branché.
/// </summary>
public static unsafe class VoCoreDevices
{
    // Numéro de série lu pour chaque port : un écran déjà ouvert (par AviX Mirror ou SimHub) ne peut pas
    // être relu, on garde alors le dernier numéro connu.
    static readonly Dictionary<string, string> SerialByPort = new();
    static readonly object Lock = new();

    /// <summary>Port USB d'un appareil, ex. « 1-4.2 » (bus puis ports).</summary>
    internal static string PortOf(IntPtr device)
    {
        var ports = stackalloc byte[8];
        int n = LibUsb.libusb_get_port_numbers(device, ports, 8);
        var parts = new List<string>();
        for (int i = 0; i < n; i++)
            parts.Add(ports[i].ToString());
        return $"{LibUsb.libusb_get_bus_number(device)}-{(parts.Count > 0 ? string.Join(".", parts) : "0")}";
    }

    /// <summary>Numéro de série (chaîne vide s'il n'y en a pas ou si l'écran ne peut pas être lu).</summary>
    internal static string SerialOf(IntPtr handle, byte index)
    {
        if (handle == IntPtr.Zero || index == 0)
            return "";
        var buffer = stackalloc byte[128];
        int n = LibUsb.libusb_get_string_descriptor_ascii(handle, index, buffer, 128);
        return n > 0 ? Marshal.PtrToStringAnsi((IntPtr)buffer, n).Trim() : "";
    }

    internal static void Remember(string port, string serial)
    {
        lock (Lock)
            SerialByPort[port] = serial;
    }

    internal static string? KnownSerial(string port)
    {
        lock (Lock)
            return SerialByPort.TryGetValue(port, out var serial) ? serial : null;
    }

    /// <summary>
    /// Parcourt les écrans VoCore (VID/PID donnés) d'un contexte libusb. <paramref name="visit"/> reçoit l'appareil,
    /// son port et l'index de son numéro de série ; il retourne vrai pour arrêter le parcours.
    /// </summary>
    internal static void Scan(IntPtr context, ushort vid, ushort pid, Func<IntPtr, string, byte, bool> visit)
    {
        nint count = LibUsb.libusb_get_device_list(context, out var list);
        if (count < 0 || list == IntPtr.Zero)
            return;
        try
        {
            var descriptor = stackalloc byte[18];
            for (int i = 0; i < count; i++)
            {
                var device = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                if (LibUsb.libusb_get_device_descriptor(device, descriptor) < 0)
                    continue;
                ushort v = (ushort)(descriptor[8] | descriptor[9] << 8);
                ushort p = (ushort)(descriptor[10] | descriptor[11] << 8);
                if (v != vid || p != pid)
                    continue;
                if (visit(device, PortOf(device), descriptor[16]))
                    break;
            }
        }
        finally
        {
            LibUsb.libusb_free_device_list(list, 1);
        }
    }

    /// <summary>Écrans VoCore branchés, triés par port. Liste vide si libusb est absent.</summary>
    public static List<VoCoreDeviceInfo> List(Settings s)
    {
        var result = new List<VoCoreDeviceInfo>();
        IntPtr context = IntPtr.Zero;
        try
        {
            if (LibUsb.libusb_init(out context) < 0)
                return result;
            Scan(context, VoCoreUsbOutput.VendorOf(s), VoCoreUsbOutput.ProductOf(s), (device, port, serialIndex) =>
            {
                string serial = "";
                if (LibUsb.libusb_open(device, out var handle) == 0)
                {
                    serial = SerialOf(handle, serialIndex);
                    LibUsb.libusb_close(handle);
                    Remember(port, serial);
                }
                else
                {
                    serial = KnownSerial(port) ?? ""; // écran déjà utilisé : dernier numéro connu
                }
                result.Add(new VoCoreDeviceInfo(serial, port));
                return false;
            });
        }
        catch
        {
            // libusb manquant : aucun écran listé.
        }
        finally
        {
            if (context != IntPtr.Zero)
                try { LibUsb.libusb_exit(context); } catch { }
        }
        return result.OrderBy(d => d.Port, StringComparer.Ordinal).ToList();
    }
}
