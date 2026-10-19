using System.Runtime.InteropServices;

namespace AviXMirror.Output;

/// <summary>
/// Accès minimal à libusb-1.0 (fourni à côté de l'exe). libusb utilise le pilote USB déjà installé
/// pour le VoCore (WinUSB / libusbK, signé, celui qu'utilise SimHub) : aucun pilote d'écran n'est nécessaire.
/// </summary>
internal static unsafe class LibUsb
{
    const string Dll = "libusb-1.0.dll";

    [DllImport(Dll)] public static extern int libusb_init(out IntPtr ctx);
    [DllImport(Dll)] public static extern void libusb_exit(IntPtr ctx);
    [DllImport(Dll)] public static extern IntPtr libusb_open_device_with_vid_pid(IntPtr ctx, ushort vendorId, ushort productId);
    [DllImport(Dll)] public static extern void libusb_close(IntPtr handle);
    [DllImport(Dll)] public static extern int libusb_claim_interface(IntPtr handle, int interfaceNumber);
    [DllImport(Dll)] public static extern int libusb_release_interface(IntPtr handle, int interfaceNumber);

    [DllImport(Dll)]
    public static extern int libusb_control_transfer(IntPtr handle, byte requestType, byte request,
        ushort value, ushort index, byte* data, ushort length, uint timeout);

    [DllImport(Dll)]
    public static extern int libusb_bulk_transfer(IntPtr handle, byte endpoint, byte* data, int length,
        out int transferred, uint timeout);

    // Énumération des appareils (reconnaissance de plusieurs VoCore).
    [DllImport(Dll)] public static extern nint libusb_get_device_list(IntPtr ctx, out IntPtr list);
    [DllImport(Dll)] public static extern void libusb_free_device_list(IntPtr list, int unrefDevices);
    [DllImport(Dll)] public static extern int libusb_get_device_descriptor(IntPtr device, byte* descriptor);
    [DllImport(Dll)] public static extern int libusb_open(IntPtr device, out IntPtr handle);
    [DllImport(Dll)] public static extern byte libusb_get_bus_number(IntPtr device);
    [DllImport(Dll)] public static extern int libusb_get_port_numbers(IntPtr device, byte* ports, int length);
    [DllImport(Dll)] public static extern int libusb_get_string_descriptor_ascii(IntPtr handle, byte index, byte* data, int length);

    [DllImport(Dll)] static extern IntPtr libusb_error_name(int code);

    public static string ErrorName(int code)
    {
        try
        {
            return Marshal.PtrToStringAnsi(libusb_error_name(code)) ?? code.ToString();
        }
        catch
        {
            return code.ToString();
        }
    }
}
