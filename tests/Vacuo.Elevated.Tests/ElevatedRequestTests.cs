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

        Assert.Equal(path, Assert.Single(request.Leftovers).Path);
        Assert.Equal(Guid.Parse(Id), request.RequestId);
    }

    [Fact]
    public void Parse_SeveralOperations_KeepsTheirOrderForOnePrompt()
    {
        ElevatedRequest request = ElevatedRequest.Parse(["windows-temp", "update-downloads", "component-cleanup", Id], Machine);

        Assert.Equal(["windows-temp", "update-downloads", "component-cleanup"], request.Leftovers.Select(item => item.Id));
    }

    [Fact]
    public void Parse_ComponentCleanup_IsAccepted()
    {
        Assert.Equal(LeftoverKind.ComponentCleanup, Assert.Single(ElevatedRequest.Parse(["component-cleanup", Id], Machine).Leftovers).Kind);
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
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", operation, Id], Machine));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\result.json")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Parse_LastArgumentThatIsNotAGuid_IsRefused(string requestId)
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", requestId], Machine));
    }

    [Fact]
    public void Parse_NoOperationOrNoRequestId_IsRefused()
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse([Id], Machine));
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp"], Machine));
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", Id, @"C:\Users"], Machine));
    }

    [Fact]
    public void Parse_SameOperationTwice_IsRefused()
    {
        Assert.Throws<RequestRefusedException>(() => ElevatedRequest.Parse(["windows-temp", "windows-temp", Id], Machine));
    }
}
