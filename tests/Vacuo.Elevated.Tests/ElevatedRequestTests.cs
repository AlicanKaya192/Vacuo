using Vacuo.Core.Scanning;

namespace Vacuo.Elevated.Tests;

public class ElevatedRequestTests
{
    private static readonly WindowsLocations Machine = new(@"C:\", @"C:\Windows", @"C:\Users\someone\AppData\Local\Temp");
    private static readonly string Id = Guid.NewGuid().ToString("D");

    [Theory]
    [InlineData("windows-temp", @"C:\Windows\Temp")]
    [InlineData("update-downloads", @"C:\Windows\SoftwareDistribution\Download")]
    [InlineData("upgrade-files-bt", @"C:\$Windows.~BT")]
    public void Parse_AdminFolder_ReturnsItsTableEntry(string operation, string path)
    {
        ElevatedRequest request = ElevatedRequest.Parse([operation, Id], Machine);

        Assert.Equal(path, request.Leftover.Path);
        Assert.Equal(Guid.Parse(Id), request.RequestId);
    }

    [Fact]
    public void Parse_ComponentCleanup_IsAccepted()
    {
        Assert.Equal(LeftoverKind.ComponentCleanup, ElevatedRequest.Parse(["component-cleanup", Id], Machine).Leftover.Kind);
    }

    [Theory]
    [InlineData("user-temp")]
    [InlineData("windows-old")]
    [InlineData("no-such-operation")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"..\..\Users")]
    public void Parse_AnythingButAnAdminOperation_IsRefused(string operation)
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse([operation, Id], Machine));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\result.json")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_RequestIdThatIsNotAGuid_IsRefused(string requestId)
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", requestId], Machine));
    }

    [Fact]
    public void Parse_MissingOrExtraArguments_IsRefused()
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp"], Machine));
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", Id, @"C:\Users"], Machine));
    }
}
