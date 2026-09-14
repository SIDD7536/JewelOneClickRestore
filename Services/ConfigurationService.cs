using System.Drawing;
using System.Text.Json;

namespace JewelOneClickRestore.Services;

public sealed class AppConfiguration
{
    public string? LastInstance { get; set; }
    public string? LastDatabase { get; set; }
    public bool ReplaceExistingDatabase { get; set; } = true;
    public bool CreateDatabaseIfMissing { get; set; } = true;
    public bool ShowSystemDatabases { get; set; }
    public int WindowWidth { get; set; } = 900;
    public int WindowHeight { get; set; } = 720;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
}

public sealed class ConfigurationService
{
    private readonly string _folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JewelOneClickRestore");

    private string ConfigFile => Path.Combine(_folder, "settings.json");

    public AppConfiguration Load()
    {
        try
        {
            if (!File.Exists(ConfigFile))
                return new AppConfiguration();

            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
        }
        catch (Exception ex)
        {
            LoggingService.Error("Failed to load configuration", ex);
            return new AppConfiguration();
        }
    }

    public void Save(AppConfiguration configuration)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(configuration, options));
        }
        catch (Exception ex)
        {
            LoggingService.Error("Failed to save configuration", ex);
        }
    }

    public static bool IsVisibleOnAnyScreen(Rectangle rectangle)
        => Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(rectangle));
}
