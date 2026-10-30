using Vacuo.Core.DiskMap;

namespace Vacuo.Core.Tests.DiskMap;

public class MapProtectionTests
{
    private static readonly MapProtection Protection = new(new MapProtectedPaths(
        DriveRoot: @"C:\",
        System: [@"C:\Windows", @"C:\pagefile.sys", @"C:\Users\Deniz\AppData\Local\Vacuo"],
        Programs: [@"C:\Program Files", @"C:\ProgramData"],
        KeepFolders: [@"C:\Users\Deniz", @"C:\Users\Deniz\Downloads", @"C:\Users\Deniz\AppData\Local"]));

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData(@"c:\windows\system32")]
    [InlineData(@"C:\Windows\")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\Users\Deniz\AppData\Local\Vacuo\journal.jsonl")]
    public void WindowsAndVacuosOwnFiles_AreSystem(string path)
    {
        Assert.Equal(MapLock.System, Protection.LockOf(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\Steam\steamapps")]
    [InlineData(@"C:\ProgramData\Docker")]
    public void InstalledPrograms_ArePrograms(string path)
    {
        Assert.Equal(MapLock.Programs, Protection.LockOf(path));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\Deniz")]
    [InlineData(@"C:\Users\Deniz\Downloads")]
    [InlineData(@"C:\Users\Deniz\AppData")]
    [InlineData(@"C:\Users\Deniz\AppData\Local")]
    public void AFolderItselfIsKept_AlsoWhenItHoldsAProtectedOne(string path)
    {
        Assert.Equal(MapLock.KeepFolder, Protection.LockOf(path));
    }

    [Theory]
    [InlineData(@"C:\Users\Deniz\Downloads\ubuntu.iso")]
    [InlineData(@"C:\Users\Deniz\Downloads\Kurs videoları")]
    [InlineData(@"C:\Users\Deniz\AppData\Local\Docker")]
    [InlineData(@"C:\Games")]
    public void WhatIsInsideAKeptFolder_CanMove(string path)
    {
        Assert.Equal(MapLock.None, Protection.LockOf(path));
    }

    // A name that only starts like a protected folder is not inside it.
    [Theory]
    [InlineData(@"C:\Windows2")]
    [InlineData(@"C:\Program Files Backup")]
    public void ASimilarName_IsNotProtected(string path)
    {
        Assert.Equal(MapLock.None, Protection.LockOf(path));
    }

    // Windows copies Default into every new account; both are shared, not the user's own.
    [Theory]
    [InlineData("Default")]
    [InlineData("Public")]
    public void ThisMachine_ProtectsTheSharedProfiles(string name)
    {
        var protection = new MapProtection(MapProtectedPaths.ForThisMachine());
        string users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;

        Assert.Equal(MapLock.System, protection.LockOf(Path.Combine(users, name, "NTUSER.DAT")));
    }

    [Fact]
    public void ThisMachine_ProtectsItsRealFolders()
    {
        var protection = new MapProtection(MapProtectedPaths.ForThisMachine());
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(MapLock.System, protection.LockOf(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.Equal(MapLock.Programs, protection.LockOf(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.Equal(MapLock.KeepFolder, protection.LockOf(profile));
        Assert.Equal(MapLock.KeepFolder, protection.LockOf(Path.GetPathRoot(profile)!));
        Assert.Equal(MapLock.KeepFolder, protection.LockOf(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        Assert.Equal(MapLock.System, protection.LockOf(AppContext.BaseDirectory));
    }
}
