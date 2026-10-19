namespace Vacuo.Core.Scanning;

/// <summary>
/// One known kind of rebuildable data. The <see cref="Id"/> doubles as the resource key
/// the interface uses for the rule's name and "what happens if removed" text.
/// </summary>
public abstract record CacheRule(string Id, Bucket Bucket, RebuildCost Cost);

/// <summary>A cache at a fixed place under a user folder, such as pip's cache.</summary>
public sealed record KnownLocationRule(
    string Id, Bucket Bucket, RebuildCost Cost, KnownRoot Root, string RelativePath)
    : CacheRule(Id, Bucket, Cost);

/// <summary>
/// User files in one folder (not its subfolders) that Vacuo only suggests, such as screenshots:
/// files with one of <paramref name="Extensions"/> that are at least <paramref name="MinimumAge"/> old.
/// </summary>
public sealed record UserFileRule(
    string Id, Bucket Bucket, RebuildCost Cost, KnownRoot Root, string RelativeFolder,
    IReadOnlySet<string> Extensions, TimeSpan MinimumAge)
    : CacheRule(Id, Bucket, Cost);

/// <summary>A folder inside a project, such as <c>node_modules</c>, recognised by its marker files.</summary>
public sealed record ProjectArtifactRule(
    string Id, Bucket Bucket, RebuildCost Cost, Func<DirectoryInfo, bool> Matches)
    : CacheRule(Id, Bucket, Cost);
