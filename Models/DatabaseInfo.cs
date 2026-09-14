namespace JewelOneClickRestore.Models;

public sealed class DatabaseInfo
{
    public string Name { get; init; } = string.Empty;
    public bool IsSystemDatabase { get; init; }
    public string StateDescription { get; init; } = string.Empty;

    public override string ToString() => Name;
}
