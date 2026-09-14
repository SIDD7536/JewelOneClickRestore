namespace JewelOneClickRestore.Models;

public sealed class RestoreVerificationResult
{
    public bool DatabaseExists { get; init; }
    public string StateDescription { get; init; } = string.Empty;
    public IReadOnlyList<string> PhysicalFiles { get; init; } = Array.Empty<string>();
    public DateTime? LatestRestoreDate { get; init; }
    public int UserTableCount { get; init; }

    public bool IsOnline => DatabaseExists &&
        string.Equals(StateDescription, "ONLINE", StringComparison.OrdinalIgnoreCase);
}
