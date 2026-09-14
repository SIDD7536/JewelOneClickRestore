using Microsoft.Win32;

namespace JewelOneClickRestore.Services;

public static class FileAssociationService
{
    private const string BakProgId = "JewelOneClickRestore.SqlBackup";

    public static void RegisterForCurrentUser()
    {
        var exe = Application.ExecutablePath;
        var quotedCommand = $"\"{exe}\" \"%1\"";

        using (var bak = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.bak"))
            bak?.SetValue(string.Empty, BakProgId);

        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{BakProgId}"))
        {
            prog?.SetValue(string.Empty, "SQL Server Backup - JEWEL One Click Restore");
            using var icon = prog?.CreateSubKey("DefaultIcon");
            icon?.SetValue(string.Empty, $"\"{exe}\",0");
            using var cmd = prog?.CreateSubKey(@"shell\open\command");
            cmd?.SetValue(string.Empty, quotedCommand);
        }

        RegisterShellVerb(@"Software\Classes\SystemFileAssociations\.bak\shell\JewelOneClickRestore", quotedCommand);
        RegisterShellVerb(@"Software\Classes\SystemFileAssociations\.zip\shell\JewelOneClickRestore", quotedCommand);

        LoggingService.Info("Registered .BAK double-click and .BAK/.ZIP 'Restore with JEWEL' shell handlers for current user.");
    }

    private static void RegisterShellVerb(string keyPath, string command)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(keyPath);
        verb?.SetValue(string.Empty, "Restore with JEWEL One Click Restore");
        verb?.SetValue("Icon", Application.ExecutablePath);
        using var cmd = verb?.CreateSubKey("command");
        cmd?.SetValue(string.Empty, command);
    }
}
