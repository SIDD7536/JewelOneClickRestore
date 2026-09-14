namespace JewelOneClickRestore.Models;

public sealed class BackupFileInfo
{
    public string BackupPath { get; init; } = string.Empty;
    public string OriginalDatabaseName { get; init; } = string.Empty;
    public IReadOnlyList<RestoreFileInfo> Files { get; init; } = Array.Empty<RestoreFileInfo>();
    public long ApproximateRequiredBytes => Files.Where(f => !f.IsLog).Sum(f => Math.Max(f.SizeBytes, 0));
}
