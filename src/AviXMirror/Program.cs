namespace AviXMirror;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "AviXMirror.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("AviX Mirror est déjà lancé.", "AviX Mirror");
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
