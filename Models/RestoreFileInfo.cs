namespace JewelOneClickRestore.Models;

public sealed class RestoreFileInfo
{
    public string LogicalName { get; init; } = string.Empty;
    public string PhysicalName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string DestinationPath { get; set; } = string.Empty;

    public bool IsLog => string.Equals(Type, "L", StringComparison.OrdinalIgnoreCase);
}
