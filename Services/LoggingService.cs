using System.Text;

namespace JewelOneClickRestore.Services;

public static class LoggingService
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JewelOneClickRestore", "Logs");

    public static string CurrentLogFile
    {
        get
        {
            Directory.CreateDirectory(LogDirectory);
            return Path.Combine(LogDirectory, $"JewelOneClickRestore-{DateTime.Now:yyyy-MM-dd}.log");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? message
            : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", detail);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(CurrentLogFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the restore utility.
        }
    }
}
