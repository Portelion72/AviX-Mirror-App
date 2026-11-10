using System.IO.Ports;

namespace AviXMirror.Output;

/// <summary>
/// LEDs des premiers rétros : WS2812B pilotées par un Arduino Nano en USB série (programme AviX
/// « AviXMirrorLeds », voir integrations/Arduino). Le Nano est trouvé tout seul (réponse « AVIXLED1 »)
/// ou sur le port choisi ; broche des LEDs envoyée au Nano (D2 par défaut).
/// </summary>
public sealed class ArduinoLeds : IDisposable
{
    public const int Baud = 115200;
    const string Hello = "AVIXLED1";

    readonly Thread _thread;
    readonly AutoResetEvent _changed = new(false);
    readonly object _lock = new();
    volatile bool _running = true;
    Settings _settings;
    Color[] _leds = Array.Empty<Color>();
    bool _dirty;
    SerialPort? _port;
    int _pinSent = -1;

    public string Status { get; private set; } = "Arduino LEDs : recherche…";

    /// <summary>Port du Nano trouvé (pour « Programmer l'Arduino »), null si aucun.</summary>
    public static string? LastPort { get; private set; }

    public ArduinoLeds(Settings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Arduino LEDs" };
        _thread.Start();
    }

    public void UpdateSettings(Settings settings)
    {
        _settings = settings;
        _changed.Set();
    }

    /// <summary>Nouvelles couleurs des LEDs, dans l'ordre de la chaîne.</summary>
    public void SetLeds(Color[] chain)
    {
        lock (_lock)
        {
            _leds = chain;
            _dirty = true;
        }
        _changed.Set();
    }

    void Run()
    {
        while (_running)
        {
            if (_port == null)
            {
                if (!Connect())
                {
                    Status = string.IsNullOrWhiteSpace(_settings.LedArduinoPort)
                        ? "Arduino LEDs : Nano introuvable (programme AviX installé ? câble USB ?)."
                        : $"Arduino LEDs : pas de réponse sur {_settings.LedArduinoPort.Trim()}.";
                    for (int i = 0; i < 30 && _running; i++)
                        Thread.Sleep(100);
                    continue;
                }
                TestSweep();
            }

            _changed.WaitOne(1000);
            if (!_running)
                break;
            try
            {
                SendPinIfNeeded();
                Color[] leds;
                lock (_lock)
                {
                    leds = _leds;
                    _dirty = false;
                }
                // Toutes les secondes au moins : le Nano éteint ses LEDs après 5 s sans nouvelles.
                Send(leds);
            }
            catch (Exception ex)
            {
                Status = "Arduino LEDs : liaison perdue — " + ex.Message;
                Close();
            }
        }
        try { Send(new Color[_leds.Length]); } catch { }
        Close();
    }

    bool Connect()
    {
        var wanted = _settings.LedArduinoPort?.Trim() ?? "";
        var ports = wanted.Length > 0 ? new[] { wanted } : Candidates();
        foreach (var name in ports)
        {
            if (!_running)
                return false;
            var port = TryOpen(name);
            if (port == null)
                continue;
            _port = port;
            _pinSent = -1;
            LastPort = name;
            Status = $"Arduino LEDs connecté ({name}, broche D{Math.Clamp(_settings.LedArduinoPin, 2, 13)}).";
            return true;
        }
        return false;
    }

    /// <summary>Ports à essayer : le dernier qui a répondu d'abord.</summary>
    static string[] Candidates()
    {
        var names = SerialPort.GetPortNames().Distinct().OrderBy(n => n).ToList();
        if (LastPort != null && names.Remove(LastPort))
            names.Insert(0, LastPort);
        return names.ToArray();
    }

    /// <summary>Ouvre le port et vérifie que le programme AviX répond (le Nano redémarre à l'ouverture).</summary>
    public static SerialPort? TryOpen(string name)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(name, Baud) { ReadTimeout = 300, WriteTimeout = 500, DtrEnable = true };
            port.Open();
            // Redémarrage du Nano à l'ouverture, puis chargeur de démarrage (≈ 1,5 s avec l'ancien).
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Thread.Sleep(1800);
            while (clock.ElapsedMilliseconds < 3500)
            {
                port.DiscardInBuffer();
                port.Write(new byte[] { 0xA5, (byte)'H' }, 0, 2);
                if (ReadLine(port, 400)?.Contains(Hello) == true)
                    return port;
            }
        }
        catch
        {
            // Port occupé ou absent : suivant.
        }
        port?.Dispose();
        return null;
    }

    static string? ReadLine(SerialPort port, int timeoutMs)
    {
        var text = new System.Text.StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                int b = port.ReadByte();
                if (b == '\n')
                    return text.ToString();
                text.Append((char)b);
            }
            catch (TimeoutException)
            {
                return text.Length > 0 ? text.ToString() : null;
            }
        }
        return text.ToString();
    }

    void SendPinIfNeeded()
    {
        int pin = Math.Clamp(_settings.LedArduinoPin, 2, 13);
        if (pin == _pinSent || _port == null)
            return;
        _port.Write(new byte[] { 0xA5, (byte)'P', (byte)pin }, 0, 3);
        Thread.Sleep(20);
        _pinSent = pin;
        Status = $"Arduino LEDs connecté ({_port.PortName}, broche D{pin}).";
        lock (_lock)
            _dirty = true;
    }

    void Send(Color[] leds)
    {
        if (_port == null)
            return;
        int n = Math.Min(leds.Length, 128);
        var frame = new byte[4 + n * 3];
        frame[0] = 0xA5;
        frame[1] = (byte)'L';
        frame[2] = (byte)n;
        byte sum = (byte)n;
        for (int i = 0; i < n; i++)
        {
            frame[3 + i * 3] = leds[i].R;
            frame[4 + i * 3] = leds[i].G;
            frame[5 + i * 3] = leds[i].B;
        }
        for (int i = 3; i < 3 + n * 3; i++)
            sum ^= frame[i];
        frame[^1] = sum;
        _port.DiscardInBuffer();
        _port.Write(frame, 0, frame.Length);
        // Attend l'accusé « K » (affichage fini) avant la suite.
        try
        {
            while (_port.ReadByte() != 'K')
            {
            }
        }
        catch (TimeoutException)
        {
            throw new IOException("le Nano ne répond plus");
        }
    }

    /// <summary>Animation au branchement : chaque LED s'allume à tour de rôle (vérifie câblage et broche).</summary>
    void TestSweep()
    {
        var s = _settings;
        int total = Math.Clamp(s.LedsPerSide, 1, 64) * 2;
        float k = Math.Clamp(s.LedBrightness, 0, 255) / 255f;
        var accent = Ui.Theme.Accent;
        var on = Color.FromArgb((int)(accent.R * k), (int)(accent.G * k), (int)(accent.B * k));
        try
        {
            SendPinIfNeeded();
            for (int i = 0; i <= total && _running; i++)
            {
                var leds = new Color[total];
                if (i < total)
                    leds[i] = on;
                Send(leds);
                Thread.Sleep(45);
            }
        }
        catch (Exception ex)
        {
            Status = "Arduino LEDs : " + ex.Message;
            Close();
        }
        lock (_lock)
            _dirty = true;
    }

    void Close()
    {
        try { _port?.Dispose(); } catch { }
        _port = null;
    }

    public void Dispose()
    {
        _running = false;
        _changed.Set();
        _thread.Join(3000);
    }
}
