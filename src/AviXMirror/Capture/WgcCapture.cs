using System.Diagnostics;
using System.Drawing.Imaging;
using AviXMirror.Util;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace AviXMirror.Capture;

/// <summary>
/// Capture d'une fenêtre (ou d'un écran) avec Windows.Graphics.Capture.
/// La capture de fenêtre récupère le contenu même s'il est caché par une autre fenêtre
/// ou situé hors de l'écran : c'est ce qui permet de ne plus afficher le rétro sur l'écran principal.
/// </summary>
public sealed class WgcCapture : IDisposable
{
    const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    readonly object _lock = new();
    readonly FrameBuffer _output;
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDirect3DDevice _winrtDevice;
    readonly GraphicsCaptureItem _item;
    readonly Direct3D11CaptureFramePool _pool;
    readonly GraphicsCaptureSession _session;

    ID3D11Texture2D? _staging;
    int _stagingWidth, _stagingHeight;
    SizeInt32 _lastSize;
    Rectangle _crop;
    long _minTicks;
    long _lastTicks;
    bool _disposed;

    /// <summary>Taille de l'image source (fenêtre ou écran complet).</summary>
    public Size SourceSize { get; private set; }

    /// <summary>Si vrai, la zone est ignorée et l'image entière est envoyée (calibrage).</summary>
    public bool FullFrame { get; set; }

    /// <summary>Déclenché quand la source disparaît (jeu fermé).</summary>
    public event Action? Closed;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public static WgcCapture ForWindow(IntPtr hwnd, FrameBuffer output) =>
        new(WgcInterop.CreateItemForWindow(hwnd), output);

    public static WgcCapture ForMonitor(IntPtr hmonitor, FrameBuffer output) =>
        new(WgcInterop.CreateItemForMonitor(hmonitor), output);

    WgcCapture(GraphicsCaptureItem item, FrameBuffer output)
    {
        _item = item;
        _output = output;

        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 },
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device!;
        _context = context!;

        using (var mt = _device.QueryInterface<ID3D11Multithread>())
            mt.SetMultithreadProtected(true);

        _winrtDevice = WgcInterop.CreateWinRTDevice(_device);

        _lastSize = item.Size;
        SourceSize = new Size(_lastSize.Width, _lastSize.Height);
        SetFps(60);

        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, PixelFormat, 2, _lastSize);
        _pool.FrameArrived += OnFrameArrived;
        _item.Closed += (_, _) => Closed?.Invoke();

        _session = _pool.CreateCaptureSession(_item);
        try { _session.IsCursorCaptureEnabled = false; } catch { /* Windows 10 < 2004 */ }
        TryRemoveBorder();
    }

    void TryRemoveBorder()
    {
        // Windows 11 : supprime le cadre jaune autour de la fenêtre capturée.
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                _ = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
                _session.IsBorderRequired = false;
            }
        }
        catch
        {
            // Non supporté : le cadre reste affiché (Windows 10).
        }
    }

    public void SetCursor(bool visible)
    {
        try { _session.IsCursorCaptureEnabled = visible; } catch { }
    }

    public void SetCrop(Rectangle crop)
    {
        lock (_lock)
            _crop = crop;
    }

    public void SetFps(int fps)
    {
        fps = Math.Clamp(fps, 5, 240);
        // Petite marge pour ne pas sauter une image sur deux à cause de la gigue.
        Interlocked.Exchange(ref _minTicks, (long)(Stopwatch.Frequency / (double)fps * 0.85));
    }

    public void Start() => _session.StartCapture();

    void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null)
                    return;

                var size = frame.ContentSize;
                if (size.Width != _lastSize.Width || size.Height != _lastSize.Height)
                {
                    _lastSize = size;
                    SourceSize = new Size(size.Width, size.Height);
                    sender.Recreate(_winrtDevice, PixelFormat, 2, size);
                }

                long now = Stopwatch.GetTimestamp();
                if (now - _lastTicks < Interlocked.Read(ref _minTicks))
                    return;
                _lastTicks = now;

                using var texture = WgcInterop.GetTexture(frame.Surface);
                var desc = texture.Description;
                var full = new Rectangle(0, 0,
                    Math.Min(size.Width, (int)desc.Width),
                    Math.Min(size.Height, (int)desc.Height));

                var region = full;
                if (!FullFrame && _crop.Width > 0 && _crop.Height > 0)
                {
                    region = Rectangle.Intersect(_crop, full);
                    if (region.Width <= 0 || region.Height <= 0)
                        region = full;
                }
                if (region.Width <= 0 || region.Height <= 0)
                    return;

                EnsureStaging(region.Width, region.Height);
                _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, texture, 0,
                    new Vortice.Mathematics.Box(region.Left, region.Top, 0, region.Right, region.Bottom, 1));

                var map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    _output.Write(region.Width, region.Height, bmp => CopyToBitmap(map.DataPointer, (int)map.RowPitch, bmp));
                }
                finally
                {
                    _context.Unmap(_staging!, 0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Capture : " + ex.Message);
            }
        }
    }

    void EnsureStaging(int width, int height)
    {
        if (_staging != null && _stagingWidth == width && _stagingHeight == height)
            return;

        _staging?.Dispose();
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
        _stagingWidth = width;
        _stagingHeight = height;
    }

    static unsafe void CopyToBitmap(IntPtr source, int sourcePitch, Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            byte* src = (byte*)source;
            byte* dst = (byte*)data.Scan0;
            for (int y = 0; y < bmp.Height; y++)
                Buffer.MemoryCopy(src + (long)y * sourcePitch, dst + (long)y * data.Stride, data.Stride, rowBytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _pool.FrameArrived -= OnFrameArrived;
            try { _session.Dispose(); } catch { }
            try { _pool.Dispose(); } catch { }
            _staging?.Dispose();
            _context.ClearState();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
