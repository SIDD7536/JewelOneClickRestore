using System.Diagnostics;

namespace JewelOneClickRestore.Services;

public static class FolderPermissionService
{
    public static bool TryGrantReadAccess(string path, string serviceName)
        => TryGrant(path, serviceName, "RX");

    public static bool TryGrantModifyAccess(string path, string serviceName)
        => TryGrant(path, serviceName, "M");

    private static bool TryGrant(string path, string serviceName, string permission)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(serviceName))
            return false;

        try
        {
            var target = Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path);

            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
                return false;

            var principals = new List<string>();
            var account = NativeServiceStatus.GetServiceAccountName(serviceName);

            if (!string.IsNullOrWhiteSpace(account))
                principals.Add(account);

            principals.Add($@"NT SERVICE\{serviceName}");

            foreach (var principal in principals.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "icacls.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    psi.ArgumentList.Add(target);
                    psi.ArgumentList.Add("/grant");
                    psi.ArgumentList.Add($"{principal}:(OI)(CI){permission}");
                    psi.ArgumentList.Add("/T");
                    psi.ArgumentList.Add("/C");
                    psi.ArgumentList.Add("/Q");

                    using var process = Process.Start(psi);
                    if (process is null)
                        continue;

                    process.WaitForExit(15000);

                    if (process.ExitCode == 0)
                    {
                        LoggingService.Info(
                            $"Granted SQL Server service permission '{permission}' on '{target}' for '{principal}'.");
                        return true;
                    }

                    LoggingService.Warn(
                        $"icacls could not grant '{permission}' on '{target}' to '{principal}'. ExitCode={process.ExitCode}");
                }
                catch (Exception ex)
                {
                    LoggingService.Warn(
                        $"Could not grant '{permission}' on '{target}' to '{principal}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Folder permission adjustment failed: {ex.Message}");
        }

        return false;
    }
}
