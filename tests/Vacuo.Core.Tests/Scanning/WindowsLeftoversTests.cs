using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class WindowsLeftoversTests
{
    private static readonly WindowsLocations Machine = new(@"C:\", @"C:\Windows", @"C:\Users\someone\AppData\Local\Temp");

    private static IReadOnlyList<WindowsLeftover> All => WindowsLeftovers.For(Machine);

    [Fact]
    public void Ids_AreUnique()
    {
        Assert.Equal(All.Count, All.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void UserTemp_IsTheOnlyItemThatNeedsNoAdministrator()
    {
        WindowsLeftover temp = Assert.Single(All, item => item.Kind == LeftoverKind.UserTemp);

        Assert.Equal(Machine.UserTemp, temp.Path);
    }

    [Fact]
    public void AdminFolders_AreInsideWindowsOrUpgradeFoldersAtTheDriveRoot()
    {
        IReadOnlyList<string?> paths = All.Where(item => item.Kind == LeftoverKind.AdminFolder).Select(item => item.Path).ToList();

        Assert.Equal(
            [@"C:\Windows\Temp", @"C:\Windows\SoftwareDistribution\Download", @"C:\$Windows.~BT", @"C:\$Windows.~WS"],
            paths);
    }

    [Fact]
    public void WindowsOld_IsLeftToWindowsOwnCleanupTool()
    {
        WindowsLeftover old = All.Single(item => item.Id == "windows-old");

        Assert.Equal(LeftoverKind.WindowsCleanupTool, old.Kind);
        Assert.Equal(@"C:\Windows.old", old.Path);
    }

    [Fact]
    public void ComponentCleanup_IsAnOperationWithoutAPath()
    {
        WindowsLeftover dism = All.Single(item => item.Kind == LeftoverKind.ComponentCleanup);

        Assert.Null(dism.Path);
    }

    [Fact]
    public void NoItem_IsTheWindowsFolderOrTheDriveItself()
    {
        Assert.DoesNotContain(All, item => item.Path is @"C:\" or @"C:\Windows" or @"C:\Windows\");
    }
}
