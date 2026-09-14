using JewelOneClickRestore.Services;

namespace JewelOneClickRestore;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, eventArgs) =>
        {
            LoggingService.Error("Unhandled UI exception", eventArgs.Exception);
            MessageBox.Show(
                $"Unexpected error:\n\n{eventArgs.Exception.Message}\n\nSee the log file for details.",
                "JEWEL One Click Restore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
                LoggingService.Error("Unhandled application exception", ex);
        };

        var startupBackup = args.FirstOrDefault(arg =>
            File.Exists(arg) &&
            (Path.GetExtension(arg).Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(arg).Equals(".zip", StringComparison.OrdinalIgnoreCase)));

        LoggingService.Info($"Application start. StartupBackup={startupBackup ?? "(none)"}");
        Application.Run(new MainForm(startupBackup));
        LoggingService.Info("Application exit");
    }
}
