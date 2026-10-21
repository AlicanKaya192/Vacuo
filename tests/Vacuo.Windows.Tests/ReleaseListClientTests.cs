using System.Net;
using System.Text;
using Vacuo.Core.Updates;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

public class ReleaseListClientTests
{
    private const string Download = "https://github.com/AlicanKaya192/Vacuo/releases/download/setup-0.5.0/";

    private static readonly string NewerFeed = $$"""
        [{ "tag_name": "setup-0.5.0", "draft": false, "prerelease": false, "body": "",
           "assets": [
             { "name": "Vacuo-0.5.0-setup.exe", "size": 100, "browser_download_url": "{{Download}}Vacuo-0.5.0-setup.exe" },
             { "name": "Vacuo-0.5.0-setup.exe.sha256", "size": 87, "browser_download_url": "{{Download}}Vacuo-0.5.0-setup.exe.sha256" } ] }]
        """;

    [Fact]
    public async Task ListFromGitHub_IsRead()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, Encoding.UTF8.GetBytes(NewerFeed));

        UpdateCheck check = await Client(handler).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Newer, check.Status);
        Assert.Equal("0.5.0", check.Offer!.Version.ToString());
    }

    [Fact]
    public async Task Check_IsOneGetThatSendsNothing()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, "[]"u8.ToArray());

        await Client(handler).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(ReleaseListClient.ListUrl, request.RequestUri!.ToString());
        Assert.Null(request.Content);
        Assert.Equal("Vacuo/0.4.0", request.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorAnswer_Fails(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Always(status, "{}"u8.ToArray());

        UpdateCheck check = await Client(handler).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, check.Status);
    }

    [Fact]
    public async Task NoConnection_IsOffline()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("No such host is known."));

        UpdateCheck check = await Client(handler).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Offline, check.Status);
    }

    [Fact]
    public async Task NoAnswerInTime_IsOffline()
    {
        var handler = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });

        UpdateCheck check = await Client(handler, TimeSpan.FromMilliseconds(100)).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Offline, check.Status);
    }

    [Fact]
    public async Task TooLongAnswer_Fails()
    {
        byte[] body = new byte[ReleaseListClient.MaximumAnswerBytes + 1];
        Array.Fill(body, (byte)' ');
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, body);

        UpdateCheck check = await Client(handler).CheckAsync(Version("0.4.0"), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, check.Status);
    }

    private static ReleaseListClient Client(StubHttpHandler handler, TimeSpan? timeout = null) =>
        new(GitHubHttp.Create(handler, "0.4.0"), timeout ?? ReleaseListClient.DefaultTimeout);

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }
}
