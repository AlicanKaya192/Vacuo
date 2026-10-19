using System.Runtime.Versioning;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class DirectorySizerTests
{
    private const int MaxPathWithoutLongPathSupport = 260;

    [Fact]
    public void Measure_EmptyDirectory_ReturnsZero()
    {
        using var tree = new TempTree();
        string directory = tree.AddDirectory("empty");

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(directory));

        Assert.Equal(new DirectorySize(0, 0), size);
    }

    [Fact]
    public void Measure_NestedFiles_SumsAllFileLengths()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\a.bin", 100);
        tree.AddFile(@"cache\sub\b.bin", 250);
        tree.AddFile(@"cache\sub\deeper\c.bin", 7);

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(tree.PathOf("cache")));

        Assert.Equal(357, size.Bytes);
    }

    [Fact]
    public void Measure_JunctionInside_DoesNotCountTarget()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\own.bin", 10);
        tree.AddFile(@"elsewhere\big.bin", 5000);
        tree.AddJunction(@"cache\link", "elsewhere");

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(tree.PathOf("cache")));

        Assert.Equal(10, size.Bytes);
    }

    [Fact]
    public void Measure_DirectorySymbolicLinkInside_DoesNotCountTarget()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\own.bin", 10);
        tree.AddFile(@"elsewhere\big.bin", 5000);
        bool created = tree.TryAddDirectorySymbolicLink(@"cache\link", "elsewhere");
        Assert.SkipUnless(created, "Windows refused to create a symbolic link (Developer Mode is off).");

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(tree.PathOf("cache")));

        Assert.Equal(10, size.Bytes);
    }

    [Fact]
    public void Measure_PathLongerThan260Characters_CountsTheFile()
    {
        using var tree = new TempTree();
        string deepFolders = string.Join('\\', Enumerable.Repeat("deep-folder-name", 20));
        string file = tree.AddFile(Path.Combine("cache", deepFolders, "x.bin"), 42);
        Assert.True(file.Length > MaxPathWithoutLongPathSupport);

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(tree.PathOf("cache")));

        Assert.Equal(42, size.Bytes);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Measure_UnreadableSubdirectory_IsCountedAsSkipped()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\readable.bin", 30);
        tree.AddFile(@"cache\locked\hidden.bin", 900);
        tree.DenyListing(@"cache\locked");

        DirectorySize size = DirectorySizer.Measure(new DirectoryInfo(tree.PathOf("cache")));

        Assert.Equal(new DirectorySize(30, 1), size);
    }
}
