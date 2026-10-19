using Vacuo.Core.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Actions;

public class OpenFileProbeTests
{
    [Fact]
    public void EnsureNothingInUse_ClosedFiles_DoesNotThrow()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\a.bin", 10);
        tree.AddFile(@"cache\sub\b.bin", 10);

        OpenFileProbe.EnsureNothingInUse(tree.PathOf("cache"));
    }

    [Fact]
    public void EnsureNothingInUse_FileOpenElsewhereInFolder_ThrowsNamingThatFile()
    {
        using var tree = new TempTree();
        tree.AddFile(@"cache\a.bin", 10);
        string busy = tree.AddFile(@"cache\sub\busy.bin", 10);
        using FileStream otherProgram = OpenLikeAnotherProgram(busy);

        var error = Assert.Throws<FileInUseException>(() => OpenFileProbe.EnsureNothingInUse(tree.PathOf("cache")));

        Assert.Equal(busy, error.FileInUse);
    }

    [Fact]
    public void EnsureNothingInUse_SingleFileOpenElsewhere_Throws()
    {
        using var tree = new TempTree();
        string busy = tree.AddFile("report.docx", 10);
        using FileStream otherProgram = OpenLikeAnotherProgram(busy);

        Assert.Throws<FileInUseException>(() => OpenFileProbe.EnsureNothingInUse(busy));
    }

    [Fact]
    public void EnsureNothingInUse_PathThatNoLongerExists_IsRefusedButNotCalledInUse()
    {
        using var tree = new TempTree();

        var error = Assert.ThrowsAny<ActionRefusedException>(() => OpenFileProbe.EnsureNothingInUse(tree.PathOf("gone.txt")));

        Assert.IsNotType<FileInUseException>(error);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void EnsureNothingInUse_UnreadableFile_KeepsTheAccessErrorAsTheCause()
    {
        using var tree = new TempTree();
        string file = tree.AddFile(@"locked\secret.bin", 10);
        tree.DenyReading(@"locked\secret.bin");

        var error = Assert.ThrowsAny<ActionRefusedException>(() => OpenFileProbe.EnsureNothingInUse(file));

        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
    }

    // Most programs share read and write but not delete, so the file cannot be moved.
    internal static FileStream OpenLikeAnotherProgram(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}
