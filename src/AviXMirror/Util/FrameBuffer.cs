using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AviXMirror.Util;

/// <summary>
/// Dernière image produite (capture ou radar), partagée entre le producteur,
/// l'écran VoCore et l'aperçu.
/// </summary>
public sealed class FrameBuffer : IDisposable
{
    readonly object _lock = new();
    Bitmap? _bitmap;
    Bitmap? _source;
    long _sequence;

    /// <summary>Traitement appliqué à chaque nouvelle image avant diffusion (ex. ATH par-dessus la caméra).</summary>
    public Action<Bitmap>? PostProcess { get; set; }

    /// <summary>
    /// Taille de l'écran : si elle est définie, une image plus petite est d'abord agrandie à cette taille
    /// (proportions gardées), puis l'ATH est dessiné par-dessus en pleine résolution. Sans cela, l'ATH
    /// dessiné sur une petite capture serait agrandi avec elle et deviendrait flou.
    /// </summary>
    public Size? UpscaleTo { get; set; }

    /// <summary>Déclenché (sur le thread du producteur) après chaque nouvelle image.</summary>
    public event Action? Updated;

    public long Sequence => Interlocked.Read(ref _sequence);

    /// <summary>
    /// Vrai pendant l'écran de veille (jeu en pause, bureau…) : les images du jeu sont ignorées,
    /// seules celles de <see cref="WriteStandby"/> sont affichées.
    /// </summary>
    public bool Held { get; set; }

    /// <summary>Écrit une nouvelle image de taille donnée via <paramref name="fill"/>.</summary>
    public void Write(int width, int height, Action<Bitmap> fill)
    {
        if (Held)
            return;
        lock (_lock)
        {
            var outSize = OutputSize(width, height);
            if (_bitmap == null || _bitmap.Width != outSize.Width || _bitmap.Height != outSize.Height)
            {
                _bitmap?.Dispose();
                _bitmap = new Bitmap(outSize.Width, outSize.Height, PixelFormat.Format32bppRgb);
            }
            if (outSize.Width == width && outSize.Height == height)
            {
                fill(_bitmap);
            }
            else
            {
                if (_source == null || _source.Width != width || _source.Height != height)
                {
                    _source?.Dispose();
                    _source = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                }
                fill(_source);
                using var g = Graphics.FromImage(_bitmap);
                // Même filtrage que l'agrandissement fait ensuite par la sortie : l'image ne change pas.
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY); // pas de bord sombre
                g.DrawImage(_source, new Rectangle(0, 0, outSize.Width, outSize.Height), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
            }
            try { PostProcess?.Invoke(_bitmap); } catch { /* l'ATH ne doit jamais bloquer l'image */ }
            _sequence++;
        }
        Updated?.Invoke();
    }

    /// <summary>Image de l'écran de veille : affichée même quand le tampon est retenu, sans ATH.</summary>
    public void WriteStandby(int width, int height, Action<Bitmap> fill)
    {
        lock (_lock)
        {
            if (_bitmap == null || _bitmap.Width != width || _bitmap.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            }
            fill(_bitmap);
            _sequence++;
        }
        Updated?.Invoke();
    }

    Size OutputSize(int width, int height)
    {
        if (UpscaleTo is not { } target || PostProcess == null || width <= 0 || height <= 0
            || (width >= target.Width && height >= target.Height))
            return new Size(width, height);
        double k = Math.Min(target.Width / (double)width, target.Height / (double)height);
        return k <= 1 ? new Size(width, height)
            : new Size(Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)));
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
            _source?.Dispose();
            _source = null;
        }
    }
}
