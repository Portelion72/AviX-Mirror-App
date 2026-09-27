using System.Drawing.Imaging;

namespace AviXMirror.Util;

/// <summary>
/// Dernière image produite (capture ou radar), partagée entre le producteur,
/// la fenêtre du rétroviseur et le serveur MJPEG.
/// </summary>
public sealed class FrameBuffer : IDisposable
{
    readonly object _lock = new();
    Bitmap? _bitmap;
    long _sequence;

    /// <summary>Traitement appliqué à chaque nouvelle image avant diffusion (ex. ATH par-dessus la caméra).</summary>
    public Action<Bitmap>? PostProcess { get; set; }

    /// <summary>Déclenché (sur le thread du producteur) après chaque nouvelle image.</summary>
    public event Action? Updated;

    public long Sequence => Interlocked.Read(ref _sequence);

    /// <summary>Écrit une nouvelle image de taille donnée via <paramref name="fill"/>.</summary>
    public void Write(int width, int height, Action<Bitmap> fill)
    {
        lock (_lock)
        {
            if (_bitmap == null || _bitmap.Width != width || _bitmap.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            }
            fill(_bitmap);
            try { PostProcess?.Invoke(_bitmap); } catch { /* l'ATH ne doit jamais bloquer l'image */ }
            _sequence++;
        }
        Updated?.Invoke();
    }

    /// <summary>Utilise l'image courante sous verrou. Retourne faux s'il n'y a pas encore d'image.</summary>
    public bool Read(Action<Bitmap> use)
    {
        lock (_lock)
        {
            if (_bitmap == null)
                return false;
            use(_bitmap);
            return true;
        }
    }

    public Bitmap? Snapshot()
    {
        lock (_lock)
            return _bitmap == null ? null : (Bitmap)_bitmap.Clone();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _sequence++;
        }
        Updated?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
