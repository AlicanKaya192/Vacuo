using Vacuo.Core.Duplicates;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Duplicates;

public class DuplicateRootsTests
{
    private const string Documents = @"C:\Users\test\Documents";
    private const string Downloads = @"C:\Users\test\Downloads";
    private static readonly ScanRootGuard Guard = new([@"C:\Windows", @"C:\Program Files"]);

    private static DuplicateRoots RootsOf(params string[] folders) => new(folders, Guard);

    [Fact]
    public void Add_NewFolder_IsAdded()
    {
        DuplicateRoots roots = RootsOf(Documents);

        Assert.Equal(AddFolderResult.Added, roots.Add(@"D:\Photos"));
        Assert.Equal([Documents, @"D:\Photos"], roots.Folders);
    }

    [Theory]
    [InlineData(Documents)]
    [InlineData(@"c:\users\test\documents\")]
    [InlineData(@"C:\Users\test\Documents\Work")]
    public void Add_FolderAlreadySearched_IsNotAddedAgain(string folder)
    {
        DuplicateRoots roots = RootsOf(Documents);

        Assert.Equal(AddFolderResult.AlreadyIncluded, roots.Add(folder));
        Assert.Equal([Documents], roots.Folders);
    }

    [Fact]
    public void Add_FolderAroundListedOnes_TakesTheirPlace()
    {
        DuplicateRoots roots = RootsOf(Documents, Downloads, @"D:\Photos");

        Assert.Equal(AddFolderResult.Added, roots.Add(@"C:\Users\test"));
        Assert.Equal([@"D:\Photos", @"C:\Users\test"], roots.Folders);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\")]
    public void Add_SystemFolder_IsRefused(string folder)
    {
        DuplicateRoots roots = RootsOf(Documents);

        Assert.Equal(AddFolderResult.SystemFolder, roots.Add(folder));
        Assert.Equal([Documents], roots.Folders);
    }

    [Fact]
    public void Remove_ListedFolder_IsRemoved()
    {
        DuplicateRoots roots = RootsOf(Documents, Downloads);

        Assert.True(roots.Remove(Downloads));
        Assert.Equal([Documents], roots.Folders);
    }

    [Fact]
    public void Remove_TheLastFolder_IsRefused()
    {
        DuplicateRoots roots = RootsOf(Documents);

        Assert.False(roots.Remove(Documents));
        Assert.Equal([Documents], roots.Folders);
    }
}
