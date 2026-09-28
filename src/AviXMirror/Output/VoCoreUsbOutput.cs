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
            if (_handle == IntPtr.Zero)
            {
                if (!TryOpen())
                {
                    Thread.Sleep(2000);
                    continue;
                }
                if (_settings.LedsEnabled)
                    LedTestSweep();
            }

            _frameReady.WaitOne(500);
            if (!_running)
                break;

            // LEDs d'abord : elles doivent réagir même si l'image ne change pas.
            try
            {
                SendLedsIfNeeded();
            }
            catch (Exception ex)
            {
                Status = "VoCore USB : erreur LEDs — " + ex.Message;
                Close();
                continue;
            }

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
        LedsOff();
        ScreenOff();
        Close();
    }

    /// <summary>
    /// Écran totalement éteint (arrêt de l'application, extinction ou mise en veille du PC) :
    /// image noire, rétroéclairage à 0 et dalle en veille. Il se rallume au prochain démarrage.
    /// </summary>
    void ScreenOff()
    {
        if (_handle == IntPtr.Zero)
            return;
        try
        {
            if (_pixels.Length > 0)
            {
                Array.Clear(_pixels);
                SendFrame();
            }
            SendCommand(new byte[] { 0x00, 0x51, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 }); // rétroéclairage à 0
            SendCommand(new byte[] { 0x00, 0x28, 0x00, 0x00, 0x00, 0x00 });             // dalle éteinte
        }
        catch
        {
            // Écran débranché : rien à éteindre.
        }
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
            // Toujours réglé : l'écran a pu être éteint (rétroéclairage à 0) à la dernière fermeture.
            byte brightness = (byte)(s.VoCoreBrightness is > 0 and <= 255 ? s.VoCoreBrightness : 255);
            SendCommand(new byte[] { 0x00, 0x51, 0x02, 0x00, 0x00, 0x00, brightness, 0x00 });

            _ledsInitialized = false;
            _ledsDirty = _leds.Length > 0;
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
        rotation = ((rotation % 360) + 360) % 360;
        var target = _nativeBitmap;
        bool direct = false;
        bool drawn = _frames.Read(frame =>
        {
            // Voie rapide : image déjà à la bonne taille, rotation par quart de tour faite pendant la
            // conversion des couleurs (évite un redimensionnement graphique à chaque image).
            bool swap = rotation % 180 != 0;
            int w = swap ? _nativeSize.Height : _nativeSize.Width, h = swap ? _nativeSize.Width : _nativeSize.Height;
            if (rotation % 90 == 0 && frame.Width == w && frame.Height == h)
            {
                ConvertRotated(frame, rotation, s.FlipHorizontal, _nativeSize, _pixels);
                direct = true;
                return;
            }
            using var g = Graphics.FromImage(target);
            FrameRenderer.Draw(g, frame, target.Size, rotation, s.FlipHorizontal, s.Stretch);
        });
        if (!drawn)
            return false;

        if (!direct)
            ConvertToRgb565(target, _pixels);
        return true;
    }

    /// <summary>
    /// Conversion en RGB565 avec rotation (0/90/180/270°, sens horaire) et miroir, pixel à pixel.
    /// Même résultat que FrameRenderer.Draw (miroir puis rotation), sans passer par GDI+.
    /// </summary>
    static unsafe void ConvertRotated(Bitmap src, int rotation, bool flip, Size native, byte[] output)
    {
        int W = native.Width, H = native.Height, w = src.Width, h = src.Height;
        var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            byte* basePtr = (byte*)data.Scan0;
            int stride = data.Stride;
            fixed (byte* outPtr = output)
            {
                ushort* dst = (ushort*)outPtr;
                for (int dy = 0; dy < H; dy++)
                {
                    for (int dx = 0; dx < W; dx++)
                    {
                        int sx, sy;
                        switch (rotation)
                        {
                            case 90: sx = dy; sy = h - 1 - dx; break;
                            case 180: sx = w - 1 - dx; sy = h - 1 - dy; break;
                            case 270: sx = w - 1 - dy; sy = dx; break;
                            default: sx = dx; sy = dy; break;
                        }
                        if (flip)
                            sx = w - 1 - sx;
                        uint p = *(uint*)(basePtr + (long)sy * stride + sx * 4);
                        *dst++ = (ushort)(((p >> 8) & 0xF800) | ((p >> 5) & 0x07E0) | ((p >> 3) & 0x001F));
                    }
                }
            }
        }
        finally
        {
            src.UnlockBits(data);
        }
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

    // ---------- LEDs WS2812B (via le bus I2C de la carte MPro) ----------
    // Protocole de github.com/Vonger/V7B_WS2812B : contrôleur de LEDs à l'adresse I2C 0x74,
    // écriture I2C = transfert de contrôle 0xB5 (tampon : adresse, longueur écrite, longueur lue,
    // registre, données) puis 0xB6 pour déclencher l'écriture.

    const byte LedI2cAddress = 0x74;
    const int I2cPacket = 16;

    readonly object _ledLock = new();
    Color[] _leds = Array.Empty<Color>();
    bool _ledsDirty, _ledsInitialized;
    LedProtocol _ledProtocolUsed;

    /// <summary>Nouvelles couleurs des LEDs, dans l'ordre de la chaîne.</summary>
    public void SetLeds(Color[] chain)
    {
        lock (_ledLock)
        {
            _leds = chain;
            _ledsDirty = true;
        }
        _frameReady.Set();
    }

    void SendLedsIfNeeded()
    {
        Color[] leds;
        lock (_ledLock)
        {
            if (!_ledsDirty || _handle == IntPtr.Zero)
                return;
            leds = _leds;
            _ledsDirty = false;
        }
        SendLeds(leds, _settings.LedProtocol);
    }

    void SendLeds(Color[] leds, LedProtocol protocol)
    {
        if (!_ledsInitialized || protocol != _ledProtocolUsed)
        {
            if (protocol == LedProtocol.Is31Compatible)
                InitIs31();
            _ledsInitialized = true;
            _ledProtocolUsed = protocol;
        }

        var data = new byte[leds.Length * 3];
        for (int i = 0; i < leds.Length; i++)
        {
            var c = leds[i];
            if (protocol == LedProtocol.Is31Compatible)
            {
                // Mode IS31FL3731 : R, G, B à partir du registre 0x24 (le firmware convertit en GRB).
                data[i * 3] = c.R; data[i * 3 + 1] = c.G; data[i * 3 + 2] = c.B;
            }
            else
            {
                // Mode complet : octets envoyés tels quels aux WS2812B, qui attendent G, R, B.
                data[i * 3] = c.G; data[i * 3 + 1] = c.R; data[i * 3 + 2] = c.B;
            }
        }

        for (int used = 0; used < data.Length; used += I2cPacket)
        {
            int size = Math.Min(I2cPacket, data.Length - used);
            var chunk = new byte[size];
            Array.Copy(data, used, chunk, 0, size);
            if (protocol == LedProtocol.Is31Compatible)
                I2cWrite(new[] { (byte)(0x24 + used) }, chunk);
            else
                I2cWrite(new[] { (byte)(used >> 8), (byte)(used & 0xff) }, chunk);
        }
    }

    /// <summary>Initialisation d'un contrôleur compatible IS31FL3731 (mode image, banque 0, toutes LEDs actives).</summary>
    void InitIs31()
    {
        I2cWrite(new byte[] { 0xfd }, new byte[] { 0x0b });  // page « fonctions »
        I2cWrite(new byte[] { 0x0a }, new byte[] { 0x00 });  // arrêt
        Thread.Sleep(10);
        I2cWrite(new byte[] { 0x0a }, new byte[] { 0x01 });  // marche
        I2cWrite(new byte[] { 0x00 }, new byte[] { 0x01 });  // mode image
        I2cWrite(new byte[] { 0x01 }, new byte[] { 0x00 });  // image = banque 0
        I2cWrite(new byte[] { 0xfd }, new byte[] { 0x00 });  // page 0
        var enable = new byte[0x12];
        Array.Fill(enable, (byte)0xff);
        I2cWrite(new byte[] { 0x00 }, enable.AsSpan(0, 16).ToArray());
        I2cWrite(new byte[] { 0x10 }, enable.AsSpan(16, 2).ToArray());
    }

    unsafe void I2cWrite(byte[] register, byte[] payload)
    {
        var buf = new byte[3 + register.Length + payload.Length];
        buf[0] = LedI2cAddress;
        buf[1] = (byte)(register.Length + payload.Length); // octets écrits (registre + données)
        buf[2] = 0;                                         // octets lus
        register.CopyTo(buf, 3);
        payload.CopyTo(buf, 3 + register.Length);
        fixed (byte* p = buf)
        {
            int r = LibUsb.libusb_control_transfer(_handle, RequestOut, 0xB5, 0, 0, p, (ushort)buf.Length, CommandTimeout);
            if (r < 0)
                throw new IOException("écriture I2C refusée (" + LibUsb.ErrorName(r) + ")");
            r = LibUsb.libusb_control_transfer(_handle, RequestIn, 0xB6, 0, 0, p, 1, CommandTimeout);
            if (r < 0)
                throw new IOException("déclenchement I2C refusé (" + LibUsb.ErrorName(r) + ")");
        }
    }

    /// <summary>
    /// Animation au branchement : chaque LED s'allume à tour de rôle dans l'ordre de la chaîne
    /// (couleur de la marque). Permet de vérifier le câblage et le sens des barrettes.
    /// </summary>
    void LedTestSweep()
    {
        var s = _settings;
        int total = Math.Clamp(s.LedsPerSide, 1, 64) * 2;
        float k = Math.Clamp(s.LedBrightness, 0, 255) / 255f;
        var accent = Ui.Theme.Accent;
        var on = Color.FromArgb((int)(accent.R * k), (int)(accent.G * k), (int)(accent.B * k));
        try
        {
            for (int i = 0; i <= total && _running; i++)
            {
                var leds = new Color[total];
                if (i < total)
                    leds[i] = on;
                SendLeds(leds, s.LedProtocol);
                Thread.Sleep(45);
            }
        }
        catch (Exception ex)
        {
            Status = "VoCore USB : LEDs indisponibles — " + ex.Message;
        }
        lock (_ledLock)
            _ledsDirty = _leds.Length > 0; // réaffiche l'état du spotter
    }

    /// <summary>Éteint les LEDs (à l'arrêt de l'application).</summary>
    void LedsOff()
    {
        Color[] leds;
        lock (_ledLock)
            leds = _leds;
        if (leds.Length == 0 || _handle == IntPtr.Zero)
            return;
        try { SendLeds(new Color[leds.Length], _settings.LedProtocol); } catch { }
    }

    public void Dispose()
    {
        _running = false;
        _frames.Updated -= OnFrame;
        _frameReady.Set();
        _thread.Join(2000);
        LedsOff();
        ScreenOff();
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
