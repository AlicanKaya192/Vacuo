using Vacuo.Core.Duplicates;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Duplicates;

public class DuplicateFinderTests
{
    private static string Write(TempTree tree, string relativePath, byte[] content)
    {
        string path = tree.PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Bytes(int length, byte fill) => Enumerable.Repeat(fill, length).ToArray();

    private static DuplicateScan Find(TempTree tree, long minimumBytes = 1) =>
        DuplicateFinder.Find([tree.Root], minimumBytes, CancellationToken.None);

    [Fact]
    public void SameContentUnderDifferentNames_IsOneGroup()
    {
        using var tree = new TempTree();
        string a = Write(tree, @"Docs\report.pdf", Bytes(100, 7));
        string b = Write(tree, @"Downloads\report (1).pdf", Bytes(100, 7));

        DuplicateGroup group = Find(tree).Groups.Single();

        Assert.Equal(100, group.FileBytes);
        Assert.Equal([a, b], group.Files.Select(file => file.Path).Order());
        Assert.Equal(100, group.WastedBytes);
    }

    [Fact]
    public void SameSizeButDifferentContent_IsNoGroup()
    {
        using var tree = new TempTree();
        Write(tree, "a.bin", Bytes(100, 1));
        Write(tree, "b.bin", Bytes(100, 2));

        Assert.Empty(Find(tree).Groups);
    }

    [Fact]
    public void SameBeginningButDifferentEnd_IsNoGroup()
    {
        using var tree = new TempTree();
        byte[] first = Bytes(DuplicateFinder.PartialHashBytes + 10, 5);
        byte[] second = (byte[])first.Clone();
        second[^1] = 6;
        Write(tree, "a.bin", first);
        Write(tree, "b.bin", second);

        Assert.Empty(Find(tree).Groups);
    }

    [Fact]
    public void ThreeCopies_WasteTwoFiles()
    {
        using var tree = new TempTree();
        Write(tree, "a.mp4", Bytes(50, 3));
        Write(tree, @"x\a.mp4", Bytes(50, 3));
        Write(tree, @"y\a.mp4", Bytes(50, 3));

        Assert.Equal(100, Find(tree).Groups.Single().WastedBytes);
    }

    [Fact]
    public void GroupsComeLargestWasteFirst()
    {
        using var tree = new TempTree();
        Write(tree, "small1", Bytes(10, 1));
        Write(tree, "small2", Bytes(10, 1));
        Write(tree, "big1", Bytes(40, 2));
        Write(tree, "big2", Bytes(40, 2));

        Assert.Equal([40L, 10L], Find(tree).Groups.Select(group => group.WastedBytes));
    }

    [Fact]
    public void FilesBelowTheMinimum_AreIgnored()
    {
        using var tree = new TempTree();
        Write(tree, "a", Bytes(10, 1));
        Write(tree, "b", Bytes(10, 1));

        Assert.Empty(Find(tree, minimumBytes: 11).Groups);
    }

    [Fact]
    public void EmptyFiles_AreNeverDuplicates()
    {
        using var tree = new TempTree();
        Write(tree, "a", []);
        Write(tree, "b", []);

        Assert.Empty(Find(tree, minimumBytes: 0).Groups);
    }

    [Fact]
    public void LinksAreNotFollowed()
    {
        using var tree = new TempTree();
        Write(tree, @"Real\a.bin", Bytes(30, 4));
        tree.AddJunction("Link", "Real");

        Assert.Empty(Find(tree).Groups);
    }

    // Reading an online-only OneDrive file would download it; such files are left alone.
    [Fact]
    public void OfflineFiles_AreNotRead()
    {
        using var tree = new TempTree();
        Write(tree, "a.bin", Bytes(30, 4));
        string online = Write(tree, "b.bin", Bytes(30, 4));
        File.SetAttributes(online, FileAttributes.Offline);

        Assert.Empty(Find(tree).Groups);
    }

    // Copies inside a code project (build output, bundled resources) belong to its tools.
    [Fact]
    public void ACodeRepository_IsNotSearched()
    {
        using var tree = new TempTree();
        Write(tree, @"site\bin\Debug\app.dll", Bytes(30, 4));
        Write(tree, @"site\bin\Release\app.dll", Bytes(30, 4));
        tree.AddDirectory(@"site\.git");

        Assert.Empty(Find(tree).Groups);
    }

    [Theory]
    [InlineData(".cache")]
    [InlineData("node_modules")]
    public void ToolAndBuildFolders_AreNotSearched(string folder)
    {
        using var tree = new TempTree();
        Write(tree, @"web\package.json", [1]);
        Write(tree, $@"web\{folder}\a.js", Bytes(30, 4));
        Write(tree, $@"web\{folder}\copy\a.js", Bytes(30, 4));

        Assert.Empty(Find(tree).Groups);
    }

    [Fact]
    public void ACancelledSearch_Stops()
    {
        using var tree = new TempTree();
        Write(tree, "a", Bytes(10, 1));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => DuplicateFinder.Find([tree.Root], 1, cancelled.Token));
    }
}
