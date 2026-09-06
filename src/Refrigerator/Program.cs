namespace Refrigerator;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Refrigerator.Singleton.6DAD34B2", out bool first);
        if (!first)
        {
            MessageBox.Show("Refrigerator já está em execução no tray.", "Refrigerator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AppPaths.Ensure();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var config = ConfigStore.Load();
        bool hidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(config, hidden));
    }
}
