namespace FruitsAtelier.Core;

public enum LibraryOrder { Title, Artist, Creator, Mode, DateAdded, RecentVisited }
public enum LibraryGrouping { None, Title, Artist, Creator, Mode, DateAdded, RecentVisited }

public sealed record LibraryOrganization(
    LibraryGrouping Group = LibraryGrouping.None,
    LibraryOrder Sort = LibraryOrder.Title,
    bool Descending = false,
    IReadOnlyDictionary<string, long>? Visits = null,
    IReadOnlyCollection<string>? Collapsed = null,
    DateTime? Today = null,
    IReadOnlyCollection<string>? Expanded = null,
    bool CollapseByDefault = false,
    IReadOnlyCollection<string>? DifficultyPaths = null);
