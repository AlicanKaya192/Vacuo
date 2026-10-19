using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class ScanRootGuardTests
{
    private static readonly ScanRootGuard ThisMachine = ScanRootGuard.ForThisMachine();

    [Theory]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86)]
    [InlineData(Environment.SpecialFolder.CommonApplicationData)]
    public void IsAllowed_SystemFolder_IsFalse(Environment.SpecialFolder folder)
    {
        Assert.False(ThisMachine.IsAllowed(Environment.GetFolderPath(folder)));
    }

    [Fact]
    public void IsAllowed_FolderInsideWindows_IsFalse()
    {
        string system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");

        Assert.False(ThisMachine.IsAllowed(system32));
    }

    [Fact]
    public void IsAllowed_DriveRootThatContainsWindows_IsFalse()
    {
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;

        Assert.False(ThisMachine.IsAllowed(systemDrive));
    }

    [Fact]
    public void IsAllowed_TempFolder_IsTrue()
    {
        Assert.True(ThisMachine.IsAllowed(Path.GetTempPath()));
    }

    [Fact]
    public void IsAllowed_FolderSharingOnlyANamePrefix_IsTrue()
    {
        var guard = new ScanRootGuard([@"D:\Fake\Windows"]);

        Assert.True(guard.IsAllowed(@"D:\Fake\WindowsProjects"));
    }

    [Fact]
    public void IsAllowed_ProtectedFolderWrittenInOtherCase_IsFalse()
    {
        var guard = new ScanRootGuard([@"D:\Fake\Windows"]);

        Assert.False(guard.IsAllowed(@"d:\fake\WINDOWS\"));
    }
}
