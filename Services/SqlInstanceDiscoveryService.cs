using JewelOneClickRestore.Models;
using Microsoft.Win32;

namespace JewelOneClickRestore.Services;

public sealed class SqlInstanceDiscoveryService
{
    public Task<IReadOnlyList<SqlInstanceInfo>> DiscoverAsync()
        => Task.Run<IReadOnlyList<SqlInstanceInfo>>(Discover);

    private static IReadOnlyList<SqlInstanceInfo> Discover()
    {
        var discovered = new Dictionary<string, SqlInstanceInfo>(StringComparer.OrdinalIgnoreCase);

        DiscoverFromRegistryView(RegistryView.Registry64, discovered);
        DiscoverFromRegistryView(RegistryView.Registry32, discovered);
        DiscoverFromServiceRegistry(discovered);

        var result = discovered.Values
            .Where(i => !string.Equals(i.Status, "Not Found", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.IsRunning)
            .ThenBy(i => i.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LoggingService.Info("Detected SQL instances: " +
                            (result.Count == 0
                                ? "(none)"
                                : string.Join(", ", result.Select(i => $"{i.ServerName} [{i.Status}]"))));

        return result;
    }

    private static void DiscoverFromRegistryView(
        RegistryView view,
        IDictionary<string, SqlInstanceInfo> discovered)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var instanceKey =
                baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");

            if (instanceKey is null)
                return;

            foreach (var instanceName in instanceKey.GetValueNames())
            {
                AddInstance(instanceName, discovered);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"SQL registry discovery ({view}) failed: {ex.Message}");
        }
    }

    private static void DiscoverFromServiceRegistry(IDictionary<string, SqlInstanceInfo> discovered)
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null)
                return;

            foreach (var serviceName in services.GetSubKeyNames())
            {
                if (string.Equals(serviceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                {
                    AddInstance("MSSQLSERVER", discovered);
                }
                else if (serviceName.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase))
                {
                    var instanceName = serviceName["MSSQL$".Length..];
                    if (!string.IsNullOrWhiteSpace(instanceName))
                        AddInstance(instanceName, discovered);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"SQL service registry discovery failed: {ex.Message}");
        }
    }

    private static void AddInstance(
        string instanceName,
        IDictionary<string, SqlInstanceInfo> discovered)
    {
        var isDefault = string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase);
        var serviceName = isDefault ? "MSSQLSERVER" : $"MSSQL${instanceName}";
        var status = NativeServiceStatus.GetStatus(serviceName);

        // Only keep instances with an actual SQL Server service on this PC.
        if (string.Equals(status, "Not Found", StringComparison.OrdinalIgnoreCase))
            return;

        var serverName = isDefault
            ? Environment.MachineName
            : $"{Environment.MachineName}\\{instanceName}";

        discovered[serverName] = new SqlInstanceInfo
        {
            InstanceName = instanceName,
            ServerName = serverName,
            ServiceName = serviceName,
            Status = status
        };
    }
}
