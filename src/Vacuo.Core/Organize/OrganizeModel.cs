namespace Vacuo.Core.Organize;

/// <summary>How the files of a folder are sorted into subfolders (B-161; by type unless the user picks another).</summary>
public enum OrganizeRule
{
    ByType,
    ByDate,
    BySource,
    ByTopic,
}

public enum FileKind
{
    Documents,
    Images,
    Videos,
    Music,
    Archives,
    Installers,
    Code,
}

/// <summary>Found only from keywords in the file name; contents are never read (B-161).</summary>
public enum Topic
{
    Invoices,
    School,
    Work,
    Travel,
    Screenshots,
}

/// <summary>A file at the top of the folder being organized. <paramref name="SourceHost"/> is the website a download came from, if Windows recorded it.</summary>
public sealed record FolderItem(string Name, DateTimeOffset LastWrite, string? SourceHost);

/// <summary>
/// Where an item goes under a rule. <paramref name="Key"/> is a <see cref="FileKind"/> or <see cref="Topic"/> name the
/// panel shows in the user's language, or a plain folder name: a month ("2026-10") or a website ("github.com").
/// </summary>
public sealed record OrganizeTarget(OrganizeRule Rule, string Key);

public sealed record PlannedMove(string Source, string Destination);

/// <summary>
/// What organizing a folder would do; nothing has moved yet. <paramref name="FolderTargets"/> tells which
/// target each folder to create is for, so it can get that target's icon.
/// </summary>
public sealed record OrganizePlan(
    string Folder,
    IReadOnlyList<PlannedMove> Moves,
    IReadOnlyList<string> FoldersToCreate,
    IReadOnlyDictionary<string, OrganizeTarget>? FolderTargets = null);

/// <summary>How the planner sees the disk, so plans can be tested without one.</summary>
public sealed record PathProbe(Func<string, bool> FileExists, Func<string, bool> FolderExists)
{
    public static PathProbe Disk { get; } = new(File.Exists, Directory.Exists);
}
