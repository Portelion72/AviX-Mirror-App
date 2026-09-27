using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using AviXMirror.Radar;
using AviXMirror.Util;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AviXMirror.Capture;

/// <summary>
/// Vraie image arrière d'Assetto Corsa, sans rétro virtuel à l'écran.
/// L'app Lua « AviX Mirror » (Custom Shaders Patch) rend une caméra arrière hors écran
/// (ac.GeometryShot) dans une texture DirectX partagée ; on ouvre cette texture sur la carte
/// graphique, on la recopie et on l'envoie au VoCore.
/// </summary>
public sealed class AcCameraSource : IDisposable
{
    // Bloc caméra de la mémoire « AviXMirror.AC.v1 » (voir integrations/AssettoCorsa/AviXMirror/AviXMirror.lua).
    const int OffVersion = 0;
    const int OffEnabled = 9044, OffWidth = 9048, OffHeight = 9052, OffFps = 9056;
    const int OffFov = 9060, OffBack = 9064, OffUp = 9068;
    const int OffHandle = 9072, OffFrame = 9080, OffStatus = 9096;
    const int ExpectedVersion = 2;
    const int StatusEncoded = 2;

    // Mode de secours (CSP sans texture partagée) : image DDS copiée par l'app Lua.
    const string ImageMapName = "AviXMirror.AC.Image.v1";
    const int ImageMax = 4 * 1024 * 1024;
    const int ImageHeader = 8; // int frame; int size;

    readonly FrameBuffer _output;
    readonly Thread _thread;
    volatile bool _running = true;
    volatile Settings _settings;

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    MemoryMappedFile? _imageFile;
    MemoryMappedViewAccessor? _imageView;
    byte[] _imageBytes = Array.Empty<byte>();
    int _lastImageFrame = -1;
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    ID3D11Texture2D? _shared, _staging;
    long _openedHandle;
    int _lastFrame = -1;
    int _heartbeat;
    bool _forceEncoded;
    readonly byte[] _halfToByte = new byte[65536];
    readonly byte[] _byteToByte = new byte[256];
    (double Exposure, double Gamma) _lutParams = (double.NaN, double.NaN);

    public void UpdateSettings(Settings settings) => _settings = settings;

    public string Status { get; private set; } = "Caméra Assetto Corsa : démarrage…";

    public AcCameraSource(FrameBuffer output, Settings settings)
    {
        _output = output;
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Caméra AC" };
        _thread.Start();
    }

    void Run()
    {
        while (_running)
        {
            var s = _settings;
            var start = Stopwatch.GetTimestamp();
            try
            {
                Step(s);
            }
            catch (Exception ex)
            {
                Status = "Caméra Assetto Corsa : " + ex.Message;
                ReleaseTexture();
                CloseMemory();
            }

            double frame = 1.0 / Math.Clamp(s.AcCamFps, 10, 60) / 2; // on interroge deux fois plus vite que la caméra
            double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0.002, frame - elapsed)));
        }
        Shutdown();
    }

    void Step(Settings s)
    {
        if (!OpenMemory())
        {
            Status = "Caméra Assetto Corsa : en attente d'AC (app Lua « AviX Mirror » pour CSP)…";
            Thread.Sleep(1000);
            return;
        }

        var view = _view!;
        if (view.ReadInt32(OffVersion) != ExpectedVersion)
        {
            Status = "Caméra Assetto Corsa : app Lua trop ancienne, cliquez « Installer l'app Assetto Corsa ».";
            Thread.Sleep(1000);
            return;
        }

        // Réglages envoyés à l'app Lua.
        int width = Math.Clamp(s.AcCamResWidth, 64, 2048), height = Math.Clamp(s.AcCamResHeight, 32, 2048);
        double hfov = Math.Clamp(s.AcCamFov, 10, 150) * Math.PI / 180;
        double vfov = 2 * Math.Atan(Math.Tan(hfov / 2) * height / width) * 180 / Math.PI;
        view.Write(OffWidth, width);
        view.Write(OffHeight, height);
        // FPS négatif = demande du mode compatibilité (texture partagée inutilisable).
        int fps = Math.Clamp(s.AcCamFps, 10, 60);
        view.Write(OffFps, _forceEncoded ? -fps : fps);
        view.Write(OffFov, (float)vfov);
        view.Write(OffBack, (float)s.AcCamBack);
        view.Write(OffUp, (float)s.AcCamUp);
        // Compteur « vivant » : l'app Lua coupe la caméra s'il n'évolue plus (AviX Mirror fermé).
        _heartbeat = _heartbeat % 1_000_000 + 1;
        view.Write(OffEnabled, _heartbeat);

        int status = view.ReadInt32(OffStatus);
        long handle = view.ReadInt64(OffHandle);
        if (status < 0)
        {
            Status = "Caméra Assetto Corsa : erreur dans l'app Lua (voir l'app « Lua Debug » de CSP).";
            return;
        }
        if (status == StatusEncoded)
        {
            ReadEncodedImage(s);
            return;
        }
        if (handle == 0)
        {
            Status = "Caméra Assetto Corsa : préparation de la caméra…";
            ReleaseTexture();
            return;
        }

        EnsureDevice();
        if (handle != _openedHandle)
        {
            try
            {
                OpenTexture(handle);
            }
            catch (Exception ex)
            {
                // Texture non partageable entre processus (certaines versions de CSP) : mode compatibilité.
                ReleaseTexture();
                _forceEncoded = true;
                Status = "Caméra Assetto Corsa : texture partagée inaccessible (" + ex.Message + "), passage en mode compatibilité…";
                return;
            }
        }

        int frame = view.ReadInt32(OffFrame);
        if (frame == _lastFrame)
            return;
        _lastFrame = frame;

        _context!.CopyResource(_staging!, _shared!);
        var desc = _staging!.Description;
        var map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            bool mirror = s.AcCamMirror;
            BuildLut(s.AcCamExposure, s.AcCamGamma);
            if (desc.Format == Format.R16G16B16A16_Float)
            {
                _output.Write((int)desc.Width, (int)desc.Height, bmp => CopyHalf(map.DataPointer, (int)map.RowPitch, bmp, mirror));
            }
            else
            {
                bool bgr = desc.Format is Format.B8G8R8A8_UNorm or Format.B8G8R8X8_UNorm;
                _output.Write((int)desc.Width, (int)desc.Height, bmp => Copy8(map.DataPointer, (int)map.RowPitch, bmp, mirror, bgr));
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        Status = $"Caméra Assetto Corsa : {desc.Width}x{desc.Height}, image n° {frame}.";
    }

    /// <summary>Mode compatibilité : lit l'image DDS non compressée écrite par l'app Lua.</summary>
    void ReadEncodedImage(Settings s)
    {
        if (_imageView == null)
        {
            try
            {
                _imageFile = MemoryMappedFile.OpenExisting(ImageMapName, MemoryMappedFileRights.Read);
                _imageView = _imageFile.CreateViewAccessor(0, ImageHeader + ImageMax, MemoryMappedFileAccess.Read);
            }
            catch
            {
                _imageFile?.Dispose();
                _imageFile = null;
                Status = "Caméra Assetto Corsa (mode compatibilité) : en attente de l'image…";
                return;
            }
        }

        int frame = _imageView.ReadInt32(0);
        int size = _imageView.ReadInt32(4);
        if (frame == _lastImageFrame || size <= 128 || size > ImageMax)
            return;
        _lastImageFrame = frame;
        if (_imageBytes.Length < size)
            _imageBytes = new byte[size];
        _imageView.ReadArray(ImageHeader, _imageBytes, 0, size);

        if (!TryDecodeDds(_imageBytes, size, s, out int w, out int h))
        {
            Status = "Caméra Assetto Corsa (mode compatibilité) : format d'image non reconnu.";
            return;
        }
        Status = $"Caméra Assetto Corsa (mode compatibilité, mettez CSP à jour pour plus de fluidité) : {w}x{h}.";
    }

    /// <summary>Décode une image DDS 32 bits non compressée (RGBA ou BGRA) vers le tampon de sortie.</summary>
    unsafe bool TryDecodeDds(byte[] dds, int size, Settings s, out int width, out int height)
    {
        width = height = 0;
        if (dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
            return false;
        int flags = BitConverter.ToInt32(dds, 8);
        height = BitConverter.ToInt32(dds, 12);
        width = BitConverter.ToInt32(dds, 16);
        int pitchOrSize = BitConverter.ToInt32(dds, 20);
        int fourCC = BitConverter.ToInt32(dds, 84);
        int bitCount = BitConverter.ToInt32(dds, 88);
        uint redMask = BitConverter.ToUInt32(dds, 92);

        int offset = 128;
        bool bgr;
        if (fourCC == 0x30315844) // « DX10 »
        {
            int dxgi = BitConverter.ToInt32(dds, 128);
            offset += 20;
            if (dxgi is 28 or 29 or 27) bgr = false;          // R8G8B8A8
            else if (dxgi is 87 or 88 or 91 or 93) bgr = true; // B8G8R8A8 / B8G8R8X8
            else return false;
        }
        else
        {
            if (bitCount != 32)
                return false;
            bgr = redMask == 0x00FF0000;
        }

        int pitch = (flags & 0x8) != 0 && pitchOrSize >= width * 4 ? pitchOrSize : width * 4;
        if (width <= 0 || height <= 0 || offset + (long)pitch * height > size)
            return false;

        int w = width, h = height;
        bool mirror = s.AcCamMirror;
        fixed (byte* p = dds)
        {
            var src = (IntPtr)(p + offset);
            BuildLut(s.AcCamExposure, s.AcCamGamma);
            _output.Write(w, h, bmp => Copy8(src, pitch, bmp, mirror, bgr));
        }
        return true;
    }

    bool OpenMemory()
    {
        if (_view != null)
            return true;
        try
        {
            _file = MemoryMappedFile.OpenExisting(AcTelemetry.MapName, MemoryMappedFileRights.ReadWrite);
            _view = _file.CreateViewAccessor(0, AcTelemetry.Size, MemoryMappedFileAccess.ReadWrite);
            return true;
        }
        catch
        {
            CloseMemory();
            return false;
        }
    }

    void CloseMemory()
    {
        _view?.Dispose();
        _file?.Dispose();
        _view = null;
        _file = null;
    }

    void EnsureDevice()
    {
        if (_device != null)
            return;
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device;
        _context = context;
    }

    void OpenTexture(long handle)
    {
        ReleaseTexture();
        _shared = _device!.OpenSharedResource<ID3D11Texture2D>(new IntPtr(handle));
        var desc = _shared.Description;
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        desc.MiscFlags = ResourceOptionFlags.None;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.SampleDescription = new SampleDescription(1, 0);
        _staging = _device.CreateTexture2D(desc);
        _openedHandle = handle;
        _lastFrame = -1;
    }

    void ReleaseTexture()
    {
        _staging?.Dispose();
        _shared?.Dispose();
        _staging = null;
        _shared = null;
        _openedHandle = 0;
    }

    /// <summary>
    /// Tables de conversion (demi-flottant -> octet et octet -> octet) avec exposition et gamma :
    /// sortie = (entrée × exposition) ^ (1 / gamma), limitée à 0..1.
    /// </summary>
    void BuildLut(double exposure, double gamma)
    {
        exposure = Math.Clamp(exposure, 0.1, 8);
        gamma = Math.Clamp(gamma, 0.2, 5);
        if ((exposure, gamma) == _lutParams)
            return;
        static byte Map(double v, double exposure, double gamma) =>
            (byte)Math.Round(Math.Pow(Math.Clamp(v * exposure, 0, 1), 1 / gamma) * 255);
        for (int i = 0; i < 65536; i++)
        {
            float v = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            _halfToByte[i] = float.IsNaN(v) || v <= 0 ? (byte)0 : Map(v, exposure, gamma);
        }
        for (int i = 0; i < 256; i++)
            _byteToByte[i] = Map(i / 255.0, exposure, gamma);
        _lutParams = (exposure, gamma);
    }

    unsafe void CopyHalf(IntPtr source, int pitch, Bitmap bmp, bool mirror)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            fixed (byte* lut = _halfToByte)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    ushort* src = (ushort*)((byte*)source + (long)y * pitch);
                    uint* dst = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        ushort* p = src + x * 4;
                        uint pixel = ((uint)lut[p[0]] << 16) | ((uint)lut[p[1]] << 8) | lut[p[2]];
                        dst[mirror ? bmp.Width - 1 - x : x] = pixel;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    unsafe void Copy8(IntPtr source, int pitch, Bitmap bmp, bool mirror, bool bgr)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            fixed (byte* lut = _byteToByte)
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* src = (byte*)source + (long)y * pitch;
                uint* dst = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte* p = src + x * 4;
                    uint pixel = bgr
                        ? ((uint)lut[p[2]] << 16) | ((uint)lut[p[1]] << 8) | lut[p[0]]
                        : ((uint)lut[p[0]] << 16) | ((uint)lut[p[1]] << 8) | lut[p[2]];
                    dst[mirror ? bmp.Width - 1 - x : x] = pixel;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    void Shutdown()
    {
        // Demande à l'app Lua d'arrêter la caméra (économise le GPU).
        try { _view?.Write(OffEnabled, 0); } catch { }
        ReleaseTexture();
        _imageView?.Dispose();
        _imageFile?.Dispose();
        _context?.ClearState();
        _context?.Dispose();
        _device?.Dispose();
        CloseMemory();
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(2000);
    }
}
