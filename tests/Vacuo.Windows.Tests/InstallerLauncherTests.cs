using Vacuo.Core.Tests.Scanning;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

public class InstallerLauncherTests
{
    [Theory]
    [InlineData(4551)]
    [InlineData(1260)]
    [InlineData(225)]
    [InlineData(226)]
    public void WindowsRefusingTheProgram_IsBlocked(int error)
    {
        Assert.Equal(InstallerStart.Blocked, InstallerLauncher.Classify(error));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(193)]
    public void AnyOtherError_IsAFailure(int error)
    {
        Assert.Equal(InstallerStart.Failed, InstallerLauncher.Classify(error));
    }

    [Fact]
    public void MissingSetup_IsAFailure()
    {
        using var tree = new TempTree();

        Assert.Equal(InstallerStart.Failed, InstallerLauncher.Start(tree.PathOf("Vacuo-0.5.0-setup.exe"), "/VERYSILENT"));
    }
}
