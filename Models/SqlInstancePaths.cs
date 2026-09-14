namespace JewelOneClickRestore.Models;

public sealed class SqlInstancePaths
{
    public string DataPath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
}
