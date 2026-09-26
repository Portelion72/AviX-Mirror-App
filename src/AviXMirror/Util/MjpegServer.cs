using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AviXMirror.Util;

/// <summary>
/// Petit serveur HTTP qui diffuse le rétro en MJPEG :
///   http://localhost:PORT/        page plein écran
///   http://localhost:PORT/stream  flux MJPEG
///   http://localhost:PORT/snapshot.jpg
/// </summary>
public sealed class MjpegServer : IDisposable
{
    const string Boundary = "avixframe";

    readonly FrameBuffer _frames;
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts = new();
    readonly ImageCodecInfo _jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    volatile int _fps;

    public MjpegServer(FrameBuffer frames, int port, int fps)
    {
        _frames = frames;
        _fps = fps;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = AcceptLoop();
    }

    public void SetFps(int fps) => _fps = fps;

    async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }
            _ = Task.Run(() => Handle(client));
        }
    }

    async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var path = await ReadRequestPath(stream);

                switch (path)
                {
                    case "/":
                    case "/index.html":
                        await WriteResponse(stream, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(
                            "<!doctype html><html><head><meta name=viewport content='width=device-width'>" +
                            "<title>AviX Mirror</title><style>html,body{margin:0;height:100%;background:#000}" +
                            "img{width:100%;height:100%;object-fit:fill;display:block}</style></head>" +
                            "<body><img src='/stream'></body></html>"));
                        break;

                    case "/snapshot.jpg":
                        await WriteResponse(stream, "image/jpeg", Encode() ?? Array.Empty<byte>());
                        break;

                    case "/stream":
                    case "/mjpeg":
                        await Stream(stream);
                        break;

                    default:
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                            "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MJPEG : " + ex.Message);
            }
        }
    }

    static async Task<string> ReadRequestPath(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n"))
        {
            int n = await stream.ReadAsync(buffer);
            if (n <= 0)
                break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (sb.Length > 16384)
                break;
        }
        var parts = sb.ToString().Split(' ');
        var path = parts.Length > 1 ? parts[1] : "/";
        int q = path.IndexOf('?');
        return q >= 0 ? path[..q] : path;
    }

    static async Task WriteResponse(NetworkStream stream, string contentType, byte[] body)
    {
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                     "Cache-Control: no-cache\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
    }

    async Task Stream(NetworkStream stream)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nCache-Control: no-cache\r\nConnection: close\r\n" +
            $"Content-Type: multipart/x-mixed-replace; boundary={Boundary}\r\n\r\n"));

        long lastSequence = -1;
        var clock = Stopwatch.StartNew();
        while (!_cts.IsCancellationRequested)
        {
            var start = clock.Elapsed;
            long seq = _frames.Sequence;
            if (seq != lastSequence)
            {
                lastSequence = seq;
                var jpeg = Encode();
                if (jpeg != null)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n"));
                    await stream.WriteAsync(jpeg);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
                }
            }
            var wait = TimeSpan.FromSeconds(1.0 / Math.Clamp(_fps, 5, 60)) - (clock.Elapsed - start);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, _cts.Token);
        }
    }

    byte[]? Encode()
    {
        using var bmp = _frames.Snapshot();
        if (bmp == null)
            return null;
        using var ms = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
        bmp.Save(ms, _jpeg, parameters);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
