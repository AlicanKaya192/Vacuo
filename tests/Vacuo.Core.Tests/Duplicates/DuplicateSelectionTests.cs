using Vacuo.Core.Duplicates;

namespace Vacuo.Core.Tests.Duplicates;

public class DuplicateSelectionTests
{
    private static readonly DuplicateGroup Group = new(
        FileBytes: 100,
        Files: [new DuplicateFile(@"C:\a\x.pdf", DateTime.UnixEpoch), new DuplicateFile(@"C:\b\x.pdf", DateTime.UnixEpoch)]);

    [Fact]
    public void NothingIsPickedBeforehand()
    {
        var selection = new DuplicateSelection([Group]);

        Assert.Empty(selection.Picked);
        Assert.Equal(0, selection.PickedBytes);
    }

    [Fact]
    public void OneCopyCanBePicked()
    {
        var selection = new DuplicateSelection([Group]);

        Assert.True(selection.TryPick(@"C:\b\x.pdf"));

        Assert.Equal([@"C:\b\x.pdf"], selection.Picked);
        Assert.Equal(100, selection.PickedBytes);
    }

    [Fact]
    public void TheLastCopyOfAGroup_CannotBePicked()
    {
        var selection = new DuplicateSelection([Group]);
        selection.TryPick(@"C:\a\x.pdf");

        Assert.False(selection.TryPick(@"C:\b\x.pdf"));
        Assert.Equal([@"C:\a\x.pdf"], selection.Picked);
    }

    [Fact]
    public void Unpicking_FreesTheOtherCopy()
    {
        var selection = new DuplicateSelection([Group]);
        selection.TryPick(@"C:\a\x.pdf");

        selection.Unpick(@"C:\a\x.pdf");

        Assert.True(selection.TryPick(@"C:\b\x.pdf"));
    }

    [Fact]
    public void CanPick_TellsWhetherACopyMayBePicked()
    {
        var selection = new DuplicateSelection([Group]);
        Assert.True(selection.CanPick(@"C:\b\x.pdf"));

        selection.TryPick(@"C:\a\x.pdf");

        Assert.True(selection.CanPick(@"C:\a\x.pdf"));
        Assert.False(selection.CanPick(@"C:\b\x.pdf"));
        Assert.False(selection.CanPick(@"C:\elsewhere.pdf"));
    }

    [Fact]
    public void APathOutsideTheGroups_CannotBePicked()
    {
        Assert.False(new DuplicateSelection([Group]).TryPick(@"C:\elsewhere.pdf"));
    }
}
