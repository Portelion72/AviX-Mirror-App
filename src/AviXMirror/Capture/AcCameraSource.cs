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

    readonly FrameBuffer _output;
    readonly Thread _thread;
    volatile bool _running = true;
    volatile Settings _settings;

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    ID3D11Texture2D? _shared, _staging;
    long _openedHandle;
    int _lastFrame = -1;
    int _heartbeat;
    readonly byte[] _halfToByte = new byte[65536];
    double _lutGamma = double.NaN;

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
        view.Write(OffFps, Math.Clamp(s.AcCamFps, 10, 60));
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
        if (handle == 0)
        {
            Status = "Caméra Assetto Corsa : préparation de la caméra…";
            ReleaseTexture();
            return;
        }

        EnsureDevice();
        if (handle != _openedHandle)
            OpenTexture(handle);

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
            if (desc.Format == Format.R16G16B16A16_Float)
            {
                BuildLut(s.AcCamGamma);
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

    /// <summary>Table demi-flottant -> octet (avec correction gamma optionnelle).</summary>
    void BuildLut(double gamma)
    {
        gamma = Math.Clamp(gamma, 0.2, 5);
        if (gamma == _lutGamma)
            return;
        for (int i = 0; i < 65536; i++)
        {
            float v = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            if (float.IsNaN(v) || v <= 0) { _halfToByte[i] = 0; continue; }
            double c = Math.Pow(Math.Min(v, 1f), 1 / gamma);
            _halfToByte[i] = (byte)Math.Round(c * 255);
        }
        _lutGamma = gamma;
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

    static unsafe void Copy8(IntPtr source, int pitch, Bitmap bmp, bool mirror, bool bgr)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* src = (byte*)source + (long)y * pitch;
                uint* dst = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte* p = src + x * 4;
                    uint pixel = bgr
                        ? ((uint)p[2] << 16) | ((uint)p[1] << 8) | p[0]
                        : ((uint)p[0] << 16) | ((uint)p[1] << 8) | p[2];
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
