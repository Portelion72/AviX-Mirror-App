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
    Rectangle _crop2;          // deuxième zone (deux rétros côte à côte), vide sinon
    Bitmap? _dualSource;       // les deux zones copiées côte à côte, avant mise à l'échelle
    DualFit _dualFit;
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

    public void SetCrop(Rectangle crop) => SetCrops(crop, Rectangle.Empty);

    /// <summary>
    /// Zones capturées. Avec une deuxième zone, l'image envoyée met la première dans la moitié gauche
    /// et la seconde dans la moitié droite (rétros gauche et droit d'un camion).
    /// </summary>
    public void SetCrops(Rectangle crop, Rectangle crop2, DualFit fit = DualFit.Entier)
    {
        lock (_lock)
        {
            _crop = crop;
            _crop2 = crop2;
            _dualFit = fit;
        }
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

                if (!FullFrame && _crop.Width > 0 && _crop.Height > 0 && _crop2.Width > 0 && _crop2.Height > 0)
                {
                    WriteDual(texture, full);
                    return;
                }

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

    /// <summary>
    /// Deux zones : copiées côte à côte dans la texture de transfert, puis chacune mise à l'échelle dans
    /// une moitié de l'image (même hauteur, format d'une moitié de VoCore : 640 × 400).
    /// </summary>
    void WriteDual(ID3D11Texture2D texture, Rectangle full)
    {
        var a = Rectangle.Intersect(_crop, full);
        var b = Rectangle.Intersect(_crop2, full);
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
            return;
        int width = a.Width + b.Width, height = Math.Max(a.Height, b.Height);
        EnsureStaging(width, height);
        _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, texture, 0,
            new Vortice.Mathematics.Box(a.Left, a.Top, 0, a.Right, a.Bottom, 1));
        _context.CopySubresourceRegion(_staging!, 0, (uint)a.Width, 0, 0, texture, 0,
            new Vortice.Mathematics.Box(b.Left, b.Top, 0, b.Right, b.Bottom, 1));

        if (_dualSource == null || _dualSource.Width != width || _dualSource.Height != height)
        {
            _dualSource?.Dispose();
            _dualSource = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        }
        var map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            CopyToBitmap(map.DataPointer, (int)map.RowPitch, _dualSource);
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }

        // Image de sortie : hauteur de la plus grande zone, chaque moitié au format 640 × 400.
        int outH = height, half = (int)Math.Round(outH * 1.6);
        var source = _dualSource;
        var fit = _dualFit;
        _output.Write(half * 2, outH, bmp =>
        {
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(Color.Black);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            DrawFitted(g, source, new Rectangle(0, 0, a.Width, a.Height), new Rectangle(0, 0, half, outH), fit);
            DrawFitted(g, source, new Rectangle(a.Width, 0, b.Width, b.Height), new Rectangle(half, 0, half, outH), fit);
        });
    }

    /// <summary>Place une zone (rétro) dans sa moitié : entière, en remplissant (recadrée au centre) ou étirée.</summary>
    static void DrawFitted(System.Drawing.Graphics g, Bitmap source, Rectangle src, Rectangle dest, DualFit fit)
    {
        if (src.Width <= 0 || src.Height <= 0)
            return;
        switch (fit)
        {
            case DualFit.Etirer:
                g.DrawImage(source, dest, src, GraphicsUnit.Pixel);
                break;
            case DualFit.Remplir:
            {
                // Garde le centre de la zone au format de la moitié.
                double target = dest.Width / (double)dest.Height;
                var crop = src;
                if (src.Width / (double)src.Height > target)
                {
                    int w = (int)Math.Round(src.Height * target);
                    crop = new Rectangle(src.X + (src.Width - w) / 2, src.Y, w, src.Height);
                }
                else
                {
                    int h = (int)Math.Round(src.Width / target);
                    crop = new Rectangle(src.X, src.Y + (src.Height - h) / 2, src.Width, h);
                }
                g.DrawImage(source, dest, crop, GraphicsUnit.Pixel);
                break;
            }
            default:
            {
                double scale = Math.Min(dest.Width / (double)src.Width, dest.Height / (double)src.Height);
                int w = (int)Math.Round(src.Width * scale), h = (int)Math.Round(src.Height * scale);
                g.DrawImage(source, new Rectangle(dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h), src, GraphicsUnit.Pixel);
                break;
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
            _dualSource?.Dispose();
            _context.ClearState();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
