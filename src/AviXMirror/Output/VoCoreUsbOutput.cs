using System.Diagnostics;
using System.Drawing.Imaging;
using AviXMirror.Util;

namespace AviXMirror.Output;

/// <summary>
/// Envoie les images directement au VoCore par USB, comme SimHub, sans pilote d'écran Windows.
/// Protocole repris du pilote officiel VoCore (github.com/Vonger/mpro_drm) :
///   - commande : transfert de contrôle vendeur (0x40), requête 0xB0 ;
///   - image : commande {00 2C len0 len1 len2 00} puis pixels RGB565 sur l'endpoint bulk 0x02.
/// </summary>
public sealed class VoCoreUsbOutput : IDisposable
{
    const byte RequestOut = 0x40;
    const byte RequestIn = 0xC0;
    const byte EndpointOut = 0x02;
    const uint CommandTimeout = 200;
    const uint BulkTimeout = 1000;

    readonly FrameBuffer _frames;
    readonly Thread _thread;
    readonly AutoResetEvent _frameReady = new(false);
    volatile bool _running = true;
    volatile Settings _settings;

    IntPtr _context;
    IntPtr _handle;
    Size _nativeSize;
    Bitmap? _nativeBitmap;
    byte[] _pixels = Array.Empty<byte>();

    public string Status { get; private set; } = "VoCore USB : démarrage…";

    /// <summary>Taille « paysage » de l'image attendue (pour le radar).</summary>
    public Size LogicalSize { get; private set; } = new(1280, 400);

    public VoCoreUsbOutput(FrameBuffer frames, Settings settings)
    {
        _frames = frames;
        _settings = settings;
        _frames.Updated += OnFrame;
        _thread = new Thread(Run) { IsBackground = true, Name = "VoCore USB" };
        _thread.Start();
    }

    public void UpdateSettings(Settings settings) => _settings = settings;

    void OnFrame() => _frameReady.Set();

    void Run()
    {
        var clock = Stopwatch.StartNew();
        double lastFrame = double.MinValue;
        long lastSequence = -1;

        while (_running)
        {
            if (_handle == IntPtr.Zero && !TryOpen())
            {
                Thread.Sleep(2000);
                continue;
            }

            _frameReady.WaitOne(500);
            if (!_running)
                break;

            var s = _settings;
            double minInterval = 1.0 / Math.Clamp(s.TargetFps, 5, 60);
            double now = clock.Elapsed.TotalSeconds;
            if (now - lastFrame < minInterval)
            {
                Thread.Sleep(TimeSpan.FromSeconds(minInterval - (now - lastFrame)));
            }

            long seq = _frames.Sequence;
            if (seq == lastSequence)
                continue;
            lastSequence = seq;
            lastFrame = clock.Elapsed.TotalSeconds;

            try
            {
                if (!RenderNative(s))
                    continue;
                SendFrame();
            }
            catch (Exception ex)
            {
                Status = "VoCore USB : erreur — " + ex.Message;
                Close();
            }
        }
        Close();
    }

    bool TryOpen()
    {
        var s = _settings;
        try
        {
            if (_context == IntPtr.Zero)
            {
                int r = LibUsb.libusb_init(out _context);
                if (r < 0)
                {
                    _context = IntPtr.Zero;
                    Status = "VoCore USB : impossible d'initialiser libusb (" + LibUsb.ErrorName(r) + ").";
                    return false;
                }
            }

            ushort vid = ParseHex(s.VoCoreVendorId, 0xC872);
            ushort pid = ParseHex(s.VoCoreProductId, 0x1004);
            _handle = LibUsb.libusb_open_device_with_vid_pid(_context, vid, pid);
            if (_handle == IntPtr.Zero)
            {
                Status = $"VoCore USB : écran {vid:X4}:{pid:X4} introuvable ou occupé. " +
                         "Fermez SimHub (ou désactivez-y le VoCore) et vérifiez le câble.";
                return false;
            }

            int claim = LibUsb.libusb_claim_interface(_handle, 0);
            if (claim < 0)
            {
                Status = "VoCore USB : écran occupé par un autre logiciel (" + LibUsb.ErrorName(claim) + "). Fermez SimHub.";
                Close();
                return false;
            }

            uint screenId = QueryScreenId();
            _nativeSize = ResolveNativeSize(s, screenId);
            LogicalSize = _nativeSize.Height > _nativeSize.Width
                ? new Size(_nativeSize.Height, _nativeSize.Width)
                : _nativeSize;

            SendCommand(new byte[] { 0x00, 0x29, 0x00, 0x00, 0x00, 0x00 }); // sortie de veille
            if (s.VoCoreBrightness is > 0 and <= 255)
                SendCommand(new byte[] { 0x00, 0x51, 0x02, 0x00, 0x00, 0x00, (byte)s.VoCoreBrightness, 0x00 });

            Status = $"VoCore USB connecté — modèle 0x{screenId:X}, {_nativeSize.Width}x{_nativeSize.Height}.";
            return true;
        }
        catch (DllNotFoundException)
        {
            Status = "VoCore USB : libusb-1.0.dll manquant à côté de AviXMirror.exe.";
            return false;
        }
        catch (Exception ex)
        {
            Status = "VoCore USB : " + ex.Message;
            Close();
            return false;
        }
    }

    static ushort ParseHex(string? text, ushort fallback) =>
        ushort.TryParse(text?.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase),
            System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : fallback;

    /// <summary>Identifiant du modèle d'écran (0 si la requête échoue).</summary>
    unsafe uint QueryScreenId()
    {
        byte[] cmd = { 0x51, 0x02, 0x04, 0x1f, 0xfc };
        var reply = new byte[5];
        fixed (byte* c = cmd)
        fixed (byte* r = reply)
        {
            if (LibUsb.libusb_control_transfer(_handle, RequestOut, 0xB5, 0, 0, c, (ushort)cmd.Length, CommandTimeout) < 0)
                return 0;
            if (LibUsb.libusb_control_transfer(_handle, RequestIn, 0xB6, 0, 0, r, 1, CommandTimeout) < 0)
                return 0;
            if (LibUsb.libusb_control_transfer(_handle, RequestIn, 0xB7, 0, 0, r, 5, CommandTimeout) < 5)
                return 0;
        }
        return BitConverter.ToUInt32(reply, 1);
    }

    static Size ResolveNativeSize(Settings s, uint screenId)
    {
        if (s.VoCoreWidth > 0 && s.VoCoreHeight > 0)
            return new Size(s.VoCoreWidth, s.VoCoreHeight);

        // Tableau du pilote officiel ; les modèles inconnus (dont le 7,8") prennent 400x1280.
        return screenId switch
        {
            0x00000005 => new Size(480, 854),
            0x00001005 => new Size(720, 1280),
            0x00000304 or 0x00000004 or 0x00000b04 or 0x00000104 => new Size(480, 800),
            0x00000007 => new Size(800, 480),
            0x00000403 => new Size(800, 800),
            0x0000000a => new Size(1024, 600),
            _ => new Size(400, 1280),
        };
    }

    unsafe void SendCommand(byte[] cmd)
    {
        fixed (byte* c = cmd)
        {
            int r = LibUsb.libusb_control_transfer(_handle, RequestOut, 0xB0, 0, 0, c, (ushort)cmd.Length, CommandTimeout);
            if (r < 0)
                throw new IOException("commande refusée (" + LibUsb.ErrorName(r) + ")");
        }
    }

    bool RenderNative(Settings s)
    {
        if (_nativeBitmap == null || _nativeBitmap.Size != _nativeSize)
        {
            _nativeBitmap?.Dispose();
            _nativeBitmap = new Bitmap(_nativeSize.Width, _nativeSize.Height, PixelFormat.Format32bppRgb);
            _pixels = new byte[_nativeSize.Width * _nativeSize.Height * 2];
        }

        // L'image est en paysage ; si l'écran est natif en portrait, on la tourne de 90°.
        int rotation = (int)s.Rotation + (_nativeSize.Height > _nativeSize.Width ? 90 : 0);
        var target = _nativeBitmap;
        bool drawn = _frames.Read(frame =>
        {
            using var g = Graphics.FromImage(target);
            FrameRenderer.Draw(g, frame, target.Size, rotation, s.FlipHorizontal, s.Stretch);
        });
        if (!drawn)
            return false;

        ConvertToRgb565(target, _pixels);
        return true;
    }

    static unsafe void ConvertToRgb565(Bitmap bmp, byte[] output)
    {
        var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            fixed (byte* outPtr = output)
            {
                ushort* dst = (ushort*)outPtr;
                for (int y = 0; y < bmp.Height; y++)
                {
                    uint* src = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        uint p = src[x];
                        *dst++ = (ushort)(((p >> 8) & 0xF800) | ((p >> 5) & 0x07E0) | ((p >> 3) & 0x001F));
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    unsafe void SendFrame()
    {
        int len = _pixels.Length;
        SendCommand(new byte[] { 0x00, 0x2C, (byte)len, (byte)(len >> 8), (byte)(len >> 16), 0x00 });
        fixed (byte* p = _pixels)
        {
            int r = LibUsb.libusb_bulk_transfer(_handle, EndpointOut, p, len, out int sent, BulkTimeout);
            if (r < 0)
                throw new IOException("envoi de l'image échoué (" + LibUsb.ErrorName(r) + ")");
            if (sent != len)
                throw new IOException($"image incomplète ({sent}/{len} octets)");
        }
    }

    void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            try
            {
                LibUsb.libusb_release_interface(_handle, 0);
                LibUsb.libusb_close(_handle);
            }
            catch { }
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _running = false;
        _frames.Updated -= OnFrame;
        _frameReady.Set();
        _thread.Join(2000);
        Close();
        if (_context != IntPtr.Zero)
        {
            try { LibUsb.libusb_exit(_context); } catch { }
            _context = IntPtr.Zero;
        }
        _nativeBitmap?.Dispose();
        _frameReady.Dispose();
    }
}
