using Vacuo.Core.Hosting;

namespace Vacuo.Core.Tests.Hosting;

public class TrayIconInputTests
{
    private const uint MouseMove = 0x0200;
    private const uint Select = 0x0400;
    private const int IconId = 1;

    // NOTIFYICON_VERSION_4: the event in the low word, the icon's id in the high word.
    private static nint Message(uint notification) => (nint)((IconId << 16) | (int)notification);

    [Theory]
    [InlineData(TrayIconInput.LeftButtonUp)]
    [InlineData(TrayIconInput.RightButtonUp)]
    public void AClick_Toggles(uint button)
    {
        Assert.True(new TrayIconInput().Toggles(Message(button), TimeSpan.Zero));
    }

    [Fact]
    public void TheKeyboardsSelect_Toggles()
    {
        Assert.True(new TrayIconInput().Toggles(Message(TrayIconInput.KeyboardSelect), TimeSpan.Zero));
    }

    [Theory]
    [InlineData(MouseMove)]
    [InlineData(Select)]
    public void OtherNotifications_DoNothing(uint notification)
    {
        Assert.False(new TrayIconInput().Toggles(Message(notification), TimeSpan.Zero));
    }

    [Fact]
    public void ASecondKeyboardSelectRightBehind_IsTheSameKeyPress()
    {
        var input = new TrayIconInput();
        input.Toggles(Message(TrayIconInput.KeyboardSelect), TimeSpan.FromSeconds(10));

        Assert.False(input.Toggles(Message(TrayIconInput.KeyboardSelect), TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void AKeyboardSelectLater_TogglesAgain()
    {
        var input = new TrayIconInput();
        input.Toggles(Message(TrayIconInput.KeyboardSelect), TimeSpan.FromSeconds(10));

        Assert.True(input.Toggles(Message(TrayIconInput.KeyboardSelect), TimeSpan.FromSeconds(10) + TrayIconInput.RepeatWindow));
    }
}
