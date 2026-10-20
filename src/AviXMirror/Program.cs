using System.Runtime.InteropServices;

namespace AviXMirror;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottlingState
    {
        public uint Version, ControlMask, StateMask;
    }

    [DllImport("kernel32.dll")]
    static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Windows 11 ralentit les applications en arrière-plan (« mode efficacité ») : AviX Mirror doit tourner à
    /// pleine vitesse pendant que le jeu a le focus. On refuse ce ralentissement (vitesse et précision des minuteurs).
    /// </summary>
    static void DisablePowerThrottling()
    {
        try
        {
            var state = new PowerThrottlingState
            {
                Version = 1,                 // PROCESS_POWER_THROTTLING_CURRENT_VERSION
                ControlMask = 0x1 | 0x4,     // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                StateMask = 0,               // désactivés
            };
            SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state, Marshal.SizeOf<PowerThrottlingState>());
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch
        {
            // Windows 10 ancien : rien à désactiver.
        }
    }

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "AviXMirror.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("AviX Mirror est déjà lancé.", "AviX Mirror");
            return;
        }

        DisablePowerThrottling();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
