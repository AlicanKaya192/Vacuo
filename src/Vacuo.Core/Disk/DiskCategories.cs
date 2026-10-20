using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

/// <summary>Where the used space on the system drive goes, as Mint shows it (development plan, Aşama 3d A).</summary>
public enum DiskCategory
{
    AppsAndData,
    DocumentsAndMedia,
    Developer,
    AiAgents,
    SystemAndOther,
}

/// <summary>A known folder whose size counts toward one category.</summary>
public sealed record CategoryRoot(DiskCategory Category, string Path);

public sealed record CategorySize(CategoryRoot Root, DirectorySize Size);

public sealed record CategoryTotal(DiskCategory Category, long Bytes, double Fraction);

/// <summary>Bytes the scan found in one category that belong to one bucket: one stream of the flow.</summary>
public sealed record CategoryFlow(DiskCategory Category, Bucket Bucket, long Bytes);

/// <summary>
/// The used space by category, and how the scan's findings flow from those categories into the
/// buckets. What no known folder holds (Windows, the page file, other users, programs elsewhere)
/// is "system and other": the used space minus everything measured, never below zero.
/// </summary>
public sealed record DiskCategories(IReadOnlyList<CategoryTotal> Totals, IReadOnlyList<CategoryFlow> Flows)
{
    public static DiskCategories From(IReadOnlyList<CategorySize> sizes, ScanSummary scan, long usedBytes)
    {
        Dictionary<DiskCategory, long> bytes = Enum.GetValues<DiskCategory>().ToDictionary(category => category, _ => 0L);
        foreach (CategorySize size in sizes)
        {
            bytes[size.Root.Category] += size.Size.Bytes;
        }

        var flows = new Dictionary<(DiskCategory, Bucket), long>();
        foreach (ScanFinding finding in scan.Findings)
        {
            DiskCategory where = CategoryOf(finding.Path, sizes);
            // Build output belongs to development wherever the project lives (a site under Documents).
            DiskCategory category = finding.Rule is ProjectArtifactRule ? DiskCategory.Developer : where;
            if (category != where)
            {
                bytes[where] -= finding.Size.Bytes;
                bytes[category] += finding.Size.Bytes;
            }

            flows[(category, finding.Rule.Bucket)] = flows.GetValueOrDefault((category, finding.Rule.Bucket)) + finding.Size.Bytes;
        }

        long measured = bytes.Where(pair => pair.Key != DiskCategory.SystemAndOther).Sum(pair => Math.Max(0, pair.Value));
        bytes[DiskCategory.SystemAndOther] = Math.Max(0, usedBytes - measured);

        List<CategoryTotal> totals = bytes
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                long total = Math.Max(0, pair.Value);
                return new CategoryTotal(pair.Key, total, usedBytes > 0 ? (double)total / usedBytes : 0);
            })
            .ToList();

        List<CategoryFlow> flowList = flows
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key.Item1)
            .ThenBy(pair => pair.Key.Item2)
            .Select(pair => new CategoryFlow(pair.Key.Item1, pair.Key.Item2, pair.Value))
            .ToList();

        return new DiskCategories(totals, flowList);
    }

    // The deepest known folder holding the path; outside all of them it is "system and other".
    private static DiskCategory CategoryOf(string path, IReadOnlyList<CategorySize> sizes)
    {
        CategoryRoot? holder = sizes
            .Select(size => size.Root)
            .Where(root => IsInside(path, root.Path))
            .MaxBy(root => root.Path.Length);
        return holder?.Category ?? DiskCategory.SystemAndOther;
    }

    private static bool IsInside(string path, string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(folder);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
