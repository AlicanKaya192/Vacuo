using Vacuo.Core.Disk;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class CategorySizerTests
{
    private static long BytesOf(IReadOnlyList<CategorySize> sizes, string path) =>
        sizes.Single(size => size.Root.Path == path).Size.Bytes;

    [Fact]
    public void EachRootIsMeasured()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Apps\a.dll", 300);
        tree.AddFile(@"Docs\b\c.pdf", 200);
        string apps = tree.PathOf("Apps");
        string docs = tree.PathOf("Docs");

        IReadOnlyList<CategorySize> sizes = CategorySizer.Measure(
            [new CategoryRoot(DiskCategory.AppsAndData, apps), new CategoryRoot(DiskCategory.DocumentsAndMedia, docs)]);

        Assert.Equal(300, BytesOf(sizes, apps));
        Assert.Equal(200, BytesOf(sizes, docs));
    }

    [Fact]
    public void ARootInsideAnother_IsCountedOnlyOnce()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\app.dat", 100);
        tree.AddFile(@"Local\pip\cache.whl", 400);
        string local = tree.PathOf("Local");
        string pip = tree.PathOf(@"Local\pip");

        IReadOnlyList<CategorySize> sizes = CategorySizer.Measure(
            [new CategoryRoot(DiskCategory.AppsAndData, local), new CategoryRoot(DiskCategory.Developer, pip)]);

        Assert.Equal(100, BytesOf(sizes, local));
        Assert.Equal(400, BytesOf(sizes, pip));
    }

    [Fact]
    public void AMissingRoot_IsZeroAndNotSkipped()
    {
        using var tree = new TempTree();
        string missing = tree.PathOf("NotThere");

        CategorySize size = CategorySizer.Measure([new CategoryRoot(DiskCategory.AiAgents, missing)]).Single();

        Assert.Equal(new Vacuo.Core.Scanning.DirectorySize(0, 0), size.Size);
    }

    [Fact]
    public void LinksAreNotFollowed()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Elsewhere\big.bin", 900);
        tree.AddFile(@"Docs\a.txt", 10);
        tree.AddJunction(@"Docs\link", "Elsewhere");
        string docs = tree.PathOf("Docs");

        CategorySize size = CategorySizer.Measure([new CategoryRoot(DiskCategory.DocumentsAndMedia, docs)]).Single();

        Assert.Equal(10, size.Size.Bytes);
    }

    // OneDrive keeps online-only files as placeholders: their length is not on this disk.
    [Fact]
    public void OfflineFiles_TakeNoSpace()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Docs\here.txt", 50);
        string online = tree.AddFile(@"Docs\online.mp4", 700);
        File.SetAttributes(online, FileAttributes.Offline);
        string docs = tree.PathOf("Docs");

        CategorySize size = CategorySizer.Measure([new CategoryRoot(DiskCategory.DocumentsAndMedia, docs)]).Single();

        Assert.Equal(50, size.Size.Bytes);
    }
}
