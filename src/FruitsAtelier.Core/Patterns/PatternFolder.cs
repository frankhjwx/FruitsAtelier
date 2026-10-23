namespace FruitsAtelier.Core.Patterns;

/// <summary>A single-level container; each pattern belongs to exactly one folder.</summary>
public sealed record PatternFolder(Guid Id, string Name, IReadOnlyList<Guid> PatternIds)
{
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");
    public const string DefaultName = "Default";
}
