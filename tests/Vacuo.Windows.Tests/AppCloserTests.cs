namespace Vacuo.Windows.Tests;

/// <summary>Only the refusals: these tests never ask a real program to close.</summary>
public class AppCloserTests
{
    [Theory]
    [InlineData("explorer")]
    [InlineData("Vacuo")]
    [InlineData("svchost")]
    public void ProtectedProgram_IsRefused(string name)
    {
        Assert.Throws<InvalidOperationException>(() => AppCloser.RequestClose(name));
    }

    [Fact]
    public void ProgramNotRunning_AsksNothing()
    {
        Assert.Equal(0, AppCloser.RequestClose(NoSuchProgram()));
    }

    [Fact]
    public void ProgramNotRunning_CannotComeToTheFront()
    {
        Assert.False(AppCloser.BringToFront(NoSuchProgram()));
    }

    private static string NoSuchProgram() => "vacuo-no-such-program-" + Guid.NewGuid().ToString("N");
}
