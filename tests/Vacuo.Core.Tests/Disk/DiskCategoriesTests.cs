using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskCategoriesTests
{
    private static readonly CacheRule Pip = KnownCacheRules.KnownLocations.Single(rule => rule.Id == "pip-cache");
    private static readonly CacheRule NodeModules = KnownCacheRules.ProjectArtifacts.Single(rule => rule.Id == "node-modules");
    private static readonly CacheRule Screenshots = UserFileRules.All.Single(rule => rule.Id == "screenshots");

    private static readonly CategoryRoot Apps = new(DiskCategory.AppsAndData, @"C:\Users\a\AppData\Local");
    private static readonly CategoryRoot PipRoot = new(DiskCategory.Developer, @"C:\Users\a\AppData\Local\pip");
    private static readonly CategoryRoot Documents = new(DiskCategory.DocumentsAndMedia, @"C:\Users\a\Documents");
    private static readonly CategoryRoot Pictures = new(DiskCategory.DocumentsAndMedia, @"C:\Users\a\Pictures");

    private static CategorySize Size(CategoryRoot root, long bytes) => new(root, new DirectorySize(bytes, 0));

    private static ScanSummary SummaryOf(params ScanFinding[] findings) =>
        ScanSummary.Combine([new ScanResult(findings, SkippedEntries: 0)]);

    private static ScanFinding Finding(string path, CacheRule rule, long bytes) =>
        new(path, rule, new DirectorySize(bytes, 0), DateTime.UnixEpoch);

    private static long BytesOf(DiskCategories categories, DiskCategory category) =>
        categories.Totals.Single(total => total.Category == category).Bytes;

    [Fact]
    public void WhatNoKnownFolderHolds_IsSystemAndOther()
    {
        DiskCategories categories = DiskCategories.From([Size(Apps, 300), Size(Documents, 200)], SummaryOf(), usedBytes: 1000);

        Assert.Equal(300, BytesOf(categories, DiskCategory.AppsAndData));
        Assert.Equal(200, BytesOf(categories, DiskCategory.DocumentsAndMedia));
        Assert.Equal(500, BytesOf(categories, DiskCategory.SystemAndOther));
        Assert.Equal(0.5, categories.Totals.Single(total => total.Category == DiskCategory.SystemAndOther).Fraction);
    }

    [Fact]
    public void EveryCategoryIsListed_EvenWhenEmpty()
    {
        DiskCategories categories = DiskCategories.From([], SummaryOf(), usedBytes: 100);

        Assert.Equal(Enum.GetValues<DiskCategory>(), categories.Totals.Select(total => total.Category));
    }

    [Fact]
    public void SystemAndOther_IsNeverNegative()
    {
        // Sizes are file lengths; compressed folders can add up to more than the drive uses.
        DiskCategories categories = DiskCategories.From([Size(Apps, 900), Size(Documents, 300)], SummaryOf(), usedBytes: 1000);

        Assert.Equal(0, BytesOf(categories, DiskCategory.SystemAndOther));
    }

    [Fact]
    public void ProjectBuildOutput_CountsAsDeveloper_NotWhereTheProjectLives()
    {
        ScanSummary scan = SummaryOf(Finding(@"C:\Users\a\Documents\site\node_modules", NodeModules, 150));

        DiskCategories categories = DiskCategories.From([Size(Documents, 400)], scan, usedBytes: 1000);

        Assert.Equal(250, BytesOf(categories, DiskCategory.DocumentsAndMedia));
        Assert.Equal(150, BytesOf(categories, DiskCategory.Developer));
        Assert.Equal(600, BytesOf(categories, DiskCategory.SystemAndOther));
    }

    [Fact]
    public void Flows_GoFromEachFindingsCategoryToItsBucket()
    {
        ScanSummary scan = SummaryOf(
            Finding(@"C:\Users\a\AppData\Local\pip\Cache", Pip, 80),
            Finding(@"C:\Users\a\Documents\site\node_modules", NodeModules, 150),
            Finding(@"C:\Users\a\Pictures\Screenshots\a.png", Screenshots, 20));

        DiskCategories categories = DiskCategories.From(
            [Size(Apps, 100), Size(PipRoot, 80), Size(Documents, 400), Size(Pictures, 50)], scan, usedBytes: 2000);

        Assert.Equal(
            [
                new CategoryFlow(DiskCategory.DocumentsAndMedia, Bucket.Yours, 20),
                new CategoryFlow(DiskCategory.Developer, Bucket.RebuildsItself, 230),
            ],
            categories.Flows);
    }

    [Fact]
    public void AFindingOutsideEveryRoot_FlowsFromSystemAndOther()
    {
        ScanSummary scan = SummaryOf(Finding(@"D:\photos\a.png", Screenshots, 20));

        DiskCategories categories = DiskCategories.From([], scan, usedBytes: 100);

        Assert.Equal([new CategoryFlow(DiskCategory.SystemAndOther, Bucket.Yours, 20)], categories.Flows);
    }
}
