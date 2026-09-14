namespace JewelOneClickRestore.Models;

public sealed class SqlInstanceInfo
{
    public string InstanceName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public bool IsRunning => string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
        => IsRunning ? $"{ServerName}   • Running" : $"{ServerName}   • {Status.ToUpperInvariant()}";
}
