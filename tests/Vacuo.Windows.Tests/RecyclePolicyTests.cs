using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

public class RecyclePolicyTests
{
    private const uint RecycleIfPossible = 0x80;
    private const uint RenameIfExists = 0x1;

    [Fact]
    public void IsRecyclableDrive_FixedDrive_IsTrue()
    {
        Assert.True(RecyclePolicy.IsRecyclableDrive(DriveType.Fixed));
    }

    [Theory]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    public void IsRecyclableDrive_DriveWithoutRecycleBin_IsFalse(DriveType driveType)
    {
        Assert.False(RecyclePolicy.IsRecyclableDrive(driveType));
    }

    [Theory]
    [InlineData(RecycleIfPossible)]
    [InlineData(RecycleIfPossible | RenameIfExists)]
    public void AllowsDelete_ShellWillRecycle_IsTrue(uint transferFlags)
    {
        Assert.True(RecyclePolicy.AllowsDelete(transferFlags));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(RenameIfExists)]
    public void AllowsDelete_ShellWouldDeleteForGood_IsFalse(uint transferFlags)
    {
        Assert.False(RecyclePolicy.AllowsDelete(transferFlags));
    }
}
