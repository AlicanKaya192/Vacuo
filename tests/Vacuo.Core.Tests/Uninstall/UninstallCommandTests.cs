using Vacuo.Core.Uninstall;

namespace Vacuo.Core.Tests.Uninstall;

public class UninstallCommandTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Foo\\unins000.exe\" /LOG", "C:\\Program Files\\Foo\\unins000.exe", "/LOG")]
    [InlineData("\"C:\\Program Files\\Foo\\uninstall.exe\"", "C:\\Program Files\\Foo\\uninstall.exe", "")]
    [InlineData("C:\\Program Files\\Foo Bar\\uninst.exe /S", "C:\\Program Files\\Foo Bar\\uninst.exe", "/S")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", "MsiExec.exe", "/X{12345678-1234-1234-1234-123456789012}")]
    [InlineData("  C:\\Tools\\remove.exe  ", "C:\\Tools\\remove.exe", "")]
    public void SplitsIntoProgramAndArguments(string command, string program, string arguments)
    {
        Assert.Equal(new UninstallCommand(program, arguments), UninstallCommand.Parse(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\Foo\\unins000.exe")]
    public void NothingUsable_IsNull(string command)
    {
        Assert.Null(UninstallCommand.Parse(command));
    }
}
