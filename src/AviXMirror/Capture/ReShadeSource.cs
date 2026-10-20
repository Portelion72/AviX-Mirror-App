using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using AviXMirror.Radar;
using AviXMirror.Util;

namespace AviXMirror.Capture;

/// <summary>Texture proposée par l'add-on ReShade (image calculée par le jeu, ex. un rétroviseur).</summary>
public sealed record ReShadeCandidate(ulong Handle, int Width, int Height, uint Format, int Samples, int Binds, int Order)
{
    /// <summary>Identifiant enregistré dans les réglages (le handle change à chaque lancement du jeu).</summary>
    public string Key => $"{Width}x{Height}:{Format}:{Order}";

    public string Label => $"{Width} × {Height}  ·  n° {Order}{(Samples > 1 ? $"  ·  MSAA {Samples}x" : "")}";
}

/// <summary>
/// Lecture de la mémoire partagée « Local\AviXMirror.ReShade.v1 » écrite par l'add-on ReShade
/// (integrations/TruckSim/AviXMirrorReShade.cpp, disposition identique) : vignettes des textures candidates
/// et image complète de la texture choisie.
/// </summary>
public sealed class ReShadeLink : IDisposable
{
    const string MapName = "Local\\AviXMirror.ReShade.v1";
    const uint Magic = 0x52585641; // 'AVXR'
    public const int MaxCandidates = 32, ThumbW = 160, ThumbH = 100;
    const long CandidatesOffset = 256;
    const long ThumbsOffset = CandidatesOffset + MaxCandidates * 32;
    const long ImageOffset = 2L * 1024 * 1024;
    const int MaxImage = 2048;
    const long ImageSize = (long)MaxImage * MaxImage * 4;
    const long MappingSize = ImageOffset + 2 * ImageSize;

    // En-tête (voir Header dans l'add-on) : deux emplacements d'image de 24 octets à partir de 24.
    const long OMagic = 0, OVersion = 4, OFrame = 8, OCount = 12, OThumbsWanted = 16, OThumbSeq = 20, OSlots = 24, SlotSize = 24;
    const long SSelected = 0, SImageSeq = 8, SImageWidth = 12, SImageHeight = 16;

    MemoryMappedFile? _file;
    MemoryMappedViewAccessor? _view;
    readonly RetryGate _gate = new();

    /// <summary>Ouvre la mémoire partagée (faux si l'add-on n'est pas chargé dans le jeu).</summary>
    public bool Open()
    {
        if (_view != null)
            return true;
        if (!_gate.Ready)
            return false;
        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
            _view = _file.CreateViewAccessor(0, MappingSize, MemoryMappedFileAccess.ReadWrite);
            if (_view.ReadUInt32(OMagic) != Magic || _view.ReadUInt32(OVersion) != 2)
            {
                Close();
                return false;
            }
            return true;
        }
        catch
        {
            Close();
            _gate.Failed();
            return false;
        }
    }

    public uint Frame => _view?.ReadUInt32(OFrame) ?? 0;
    public uint ThumbSeq => _view?.ReadUInt32(OThumbSeq) ?? 0;

    public bool ThumbnailsWanted
    {
        set => _view?.Write(OThumbsWanted, value ? 1u : 0u);
    }

    /// <summary>Texture choisie pour l'image 1 (rétro gauche) ou 2 (rétro droit), 0 = aucune.</summary>
    public void Select(int slot, ulong handle) => _view?.Write(OSlots + slot * SlotSize + SSelected, handle);

    public List<ReShadeCandidate> Candidates()
    {
        var list = new List<ReShadeCandidate>();
        if (_view == null)
            return list;
        int count = (int)Math.Min(_view.ReadUInt32(OCount), MaxCandidates);
        for (int i = 0; i < count; i++)
        {
            long o = CandidatesOffset + i * 32L;
            list.Add(new ReShadeCandidate(_view.ReadUInt64(o), (int)_view.ReadUInt32(o + 8), (int)_view.ReadUInt32(o + 12),
                _view.ReadUInt32(o + 16), (int)_view.ReadUInt32(o + 20), (int)_view.ReadUInt32(o + 24), (int)_view.ReadUInt32(o + 28)));
        }
        return list;
    }

    /// <summary>Vignette n° <paramref name="index"/> (160 × 100).</summary>
    public Bitmap Thumbnail(int index)
    {
        var bmp = new Bitmap(ThumbW, ThumbH, PixelFormat.Format32bppRgb);
        if (_view == null)
            return bmp;
        var bytes = new byte[ThumbW * ThumbH * 4];
        _view.ReadArray(ThumbsOffset + index * (long)bytes.Length, bytes, 0, bytes.Length);
        Copy(bytes, bmp);
        return bmp;
    }

    byte[] _image = Array.Empty<byte>();

    /// <summary>Image complète de l'emplacement <paramref name="slot"/> si elle a changé depuis <paramref name="lastSeq"/> (verrou de séquence).</summary>
    public Bitmap? ReadImage(int slot, ref uint lastSeq)
    {
        if (_view == null)
            return null;
        long o = OSlots + slot * SlotSize;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint seq = _view.ReadUInt32(o + SImageSeq);
            if (seq == lastSeq || (seq & 1) != 0)
                return null;
            int w = (int)_view.ReadUInt32(o + SImageWidth), h = (int)_view.ReadUInt32(o + SImageHeight);
            if (w <= 0 || h <= 0 || w > MaxImage || h > MaxImage)
                return null;
            int length = w * h * 4;
            if (_image.Length != length)
                _image = new byte[length];
            _view.ReadArray(ImageOffset + slot * ImageSize, _image, 0, length);
            if (_view.ReadUInt32(o + SImageSeq) != seq)
                continue; // l'add-on écrivait pendant la lecture : on recommence
            lastSeq = seq;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            Copy(_image, bmp);
            return bmp;
        }
        return null;
    }

    static void Copy(byte[] bytes, Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int row = bmp.Width * 4;
            for (int y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(bytes, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    void Close()
    {
        _view?.Dispose();
        _file?.Dispose();
        _view = null;
        _file = null;
    }

    public void Dispose()
    {
        ThumbnailsWanted = false;
        Close();
    }
}

/// <summary>
/// Mode Capture par l'add-on ReShade (ETS2 / ATS en solo) : l'image du rétroviseur est prise dans la carte
/// graphique avant d'être affichée. Rien à cacher sur l'écran principal.
/// </summary>
public sealed class ReShadeSource : ICaptureSource
{
    readonly FrameBuffer _output;
    readonly ReShadeLink _link = new();
    readonly Thread _thread;
    readonly object _lock = new();
    volatile bool _running = true;
    Rectangle _crop, _crop2;
    DualFit _fit;
    string _target = "";
    bool _flipH, _flipV;

    public Size SourceSize { get; private set; }
    public bool FullFrame { get; set; }
    public string Status { get; private set; } = "ReShade : en attente de l'add-on AviX Mirror dans le jeu…";

    public ReShadeSource(FrameBuffer output)
    {
        _output = output;
        _thread = new Thread(Run) { IsBackground = true, Name = "AviX ReShade" };
        _thread.Start();
    }

    public void SetCrops(Rectangle crop, Rectangle crop2, DualFit fit = DualFit.Entier)
    {
        lock (_lock)
        {
            _crop = crop;
            _crop2 = crop2;
            _fit = fit;
        }
    }

    /// <summary>Texture choisie (clé <see cref="ReShadeCandidate.Key"/>) et retournements éventuels.</summary>
    public void Configure(string target, bool flipH, bool flipV)
    {
        lock (_lock)
        {
            _target = target ?? "";
            _flipH = flipH;
            _flipV = flipV;
        }
    }

    /// <summary>Clés des textures choisies : une, ou deux séparées par « | » (rétros gauche et droit).</summary>
    public static string[] Targets(string value) =>
        (value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2).ToArray();

    void Run()
    {
        var lastSeq = new uint[2];
        var images = new Bitmap?[2];
        long lastPick = 0;
        while (_running)
        {
            if (!_link.Open())
            {
                Status = "ReShade : add-on AviX Mirror non chargé (ReShade « add-ons » + AviXMirror.addon64 dans le dossier du jeu, solo).";
                Thread.Sleep(1000);
                continue;
            }

            string[] targets;
            bool flipH, flipV;
            lock (_lock)
            {
                targets = Targets(_target);
                flipH = _flipH;
                flipV = _flipV;
            }

            // Retrouve les textures choisies parmi les candidates (leur handle change à chaque lancement).
            long now = Environment.TickCount64;
            if (now - lastPick > 500)
            {
                lastPick = now;
                var candidates = _link.Candidates();
                var missing = new List<string>();
                for (int k = 0; k < 2; k++)
                {
                    var match = k < targets.Length ? candidates.FirstOrDefault(c => c.Key == targets[k]) : null;
                    _link.Select(k, match?.Handle ?? 0);
                    if (k < targets.Length && match == null)
                        missing.Add(targets[k]);
                }
                Status = targets.Length == 0
                    ? "ReShade connecté — choisissez l'image du rétro (onglet Capture → « Image ReShade »)."
                    : missing.Count > 0
                        ? $"ReShade : image {string.Join(", ", missing)} pas encore calculée par le jeu (au volant, rétros visibles ?)."
                        : $"ReShade : {targets.Length} image(s) de rétro reçue(s).";
            }

            bool changed = false;
            for (int k = 0; k < targets.Length; k++)
            {
                var image = _link.ReadImage(k, ref lastSeq[k]);
                if (image == null)
                    continue;
                if (flipH || flipV)
                    image.RotateFlip(flipH && flipV ? RotateFlipType.RotateNoneFlipXY : flipH ? RotateFlipType.RotateNoneFlipX : RotateFlipType.RotateNoneFlipY);
                images[k]?.Dispose();
                images[k] = image;
                changed = true;
            }
            if (!changed || images[0] == null)
            {
                Thread.Sleep(4);
                continue;
            }

            Rectangle crop, crop2;
            DualFit fit;
            lock (_lock)
            {
                crop = _crop;
                crop2 = _crop2;
                fit = _fit;
            }
            try
            {
                if (targets.Length == 2 && images[1] != null)
                {
                    // Deux textures : rétro gauche dans la moitié gauche, rétro droit dans la droite (images entières).
                    SourceSize = images[0]!.Size;
                    WriteTwo(images[0]!, images[1]!, fit);
                }
                else
                {
                    SourceSize = images[0]!.Size;
                    FrameComposer.Write(_output, images[0]!, FullFrame || crop.Width <= 0 || crop.Height <= 0, crop, crop2, fit);
                }
            }
            catch
            {
                // Image incomplète : on attend la suivante.
            }
        }
        foreach (var image in images)
            image?.Dispose();
    }

    void WriteTwo(Bitmap left, Bitmap right, DualFit fit)
    {
        int outH = Math.Max(left.Height, right.Height), half = (int)Math.Round(outH * 1.6);
        _output.Write(half * 2, outH, bmp =>
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            FrameComposer.DrawFitted(g, left, new Rectangle(0, 0, left.Width, left.Height), new Rectangle(0, 0, half, outH), fit);
            FrameComposer.DrawFitted(g, right, new Rectangle(0, 0, right.Width, right.Height), new Rectangle(half, 0, half, outH), fit);
        });
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(1000);
        _link.Select(0, 0);
        _link.Select(1, 0);
        _link.Dispose();
    }
}
