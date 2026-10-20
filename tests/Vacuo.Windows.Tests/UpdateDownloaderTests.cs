using System.Net;
using System.Security.Cryptography;
using System.Text;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

public class UpdateDownloaderTests
{
    private const string Download = "https://github.com/AlicanKaya192/vacuo/releases/download/setup-0.5.0/";
    private const string Name = "Vacuo-0.5.0-setup.exe";

    [Fact]
    public async Task GoodDownload_IsReady_AndReportsProgress()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(700_000);
        var progress = new Collected();

        DownloadResult result = await Downloader(Server(setup, Checksum(setup)), tree).DownloadAsync(Offer(setup.Length), progress, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Ready, result.Outcome);
        Assert.Equal(tree.PathOf(Name), result.InstallerPath);
        Assert.Equal(setup, File.ReadAllBytes(result.InstallerPath!));
        Assert.Equal([Name], Directory.GetFiles(tree.Root).Select(Path.GetFileName));
        Assert.True(progress.Reports.Count > 1);
        Assert.Equal(new DownloadProgress(setup.Length, setup.Length), progress.Reports[^1]);
    }

    [Fact]
    public async Task ChangedFile_IsRejected_AndLeavesNothing()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(1000);
        string checksum = Checksum(Setup(1000));

        DownloadResult result = await Downloader(Server(setup, checksum), tree).DownloadAsync(Offer(setup.Length), null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Rejected, result.Outcome);
        Assert.Null(result.InstallerPath);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task LongerThanTheReleaseSays_IsRejected()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(1000);

        DownloadResult result = await Downloader(Server(setup, Checksum(setup)), tree).DownloadAsync(Offer(setup.Length - 1), null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Rejected, result.Outcome);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task Cancelled_LeavesNothing()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(2_000_000);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new Collected(_ => cancellation.Cancel());

        DownloadResult result = await Downloader(Server(setup, Checksum(setup)), tree).DownloadAsync(Offer(setup.Length), progress, cancellation.Token);

        Assert.Equal(DownloadOutcome.Cancelled, result.Outcome);
        Assert.Single(progress.Reports);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task ErrorAnswer_Fails()
    {
        using var tree = new TempTree();
        var handler = StubHttpHandler.Always(HttpStatusCode.NotFound, "Not Found"u8.ToArray());

        DownloadResult result = await Downloader(handler, tree).DownloadAsync(Offer(1000), null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task LostConnection_IsOffline_AndLeavesNothing()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(1000);
        var handler = new StubHttpHandler((request, _) => request.RequestUri!.ToString().EndsWith(".sha256", StringComparison.Ordinal)
            ? Task.FromResult(StubHttpHandler.Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes(Checksum(setup))))
            : throw new HttpRequestException("The connection was reset."));

        DownloadResult result = await Downloader(handler, tree).DownloadAsync(Offer(setup.Length), null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Offline, result.Outcome);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task StalledDownload_IsOffline_AndLeavesNothing()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(1000);
        var handler = new StubHttpHandler((request, _) => Task.FromResult(request.RequestUri!.ToString().EndsWith(".sha256", StringComparison.Ordinal)
            ? StubHttpHandler.Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes(Checksum(setup)))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }));
        var downloader = new UpdateDownloader(GitHubHttp.Create(handler, "0.4.0"), tree.Root, TimeSpan.FromMilliseconds(100));

        DownloadResult result = await downloader.DownloadAsync(Offer(setup.Length), null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Offline, result.Outcome);
        Assert.Empty(Directory.GetFiles(tree.Root));
    }

    [Fact]
    public async Task OfferOutsideVacuosReleases_IsNotDownloaded()
    {
        using var tree = new TempTree();
        byte[] setup = Setup(1000);
        StubHttpHandler handler = Server(setup, Checksum(setup));
        var offer = Offer(setup.Length) with { Installer = new ReleaseAsset(Name, "https://example.com/" + Name, setup.Length) };

        DownloadResult result = await Downloader(handler, tree).DownloadAsync(offer, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    private static UpdateDownloader Downloader(StubHttpHandler handler, TempTree tree) =>
        new(GitHubHttp.Create(handler, "0.4.0"), tree.Root, UpdateDownloader.DefaultStallTimeout);

    private static StubHttpHandler Server(byte[] setup, string checksum) =>
        new((request, _) => Task.FromResult(request.RequestUri!.ToString().EndsWith(".sha256", StringComparison.Ordinal)
            ? StubHttpHandler.Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes(checksum))
            : StubHttpHandler.Response(HttpStatusCode.OK, setup)));

    private static UpdateOffer Offer(long size)
    {
        Assert.True(ReleaseVersion.TryParse("0.5.0", out ReleaseVersion? version));
        return new UpdateOffer(
            version!,
            new ReleaseAsset(Name, Download + Name, size),
            new ReleaseAsset(Name + ".sha256", Download + Name + ".sha256", 87),
            "");
    }

    private static byte[] Setup(int size)
    {
        byte[] content = new byte[size];
        Random.Shared.NextBytes(content);
        content[0] = (byte)'M';
        content[1] = (byte)'Z';
        return content;
    }

    private static string Checksum(byte[] content) => $"{Convert.ToHexStringLower(SHA256.HashData(content))}  {Name}";

    /// <summary>Collects progress as it is reported, on the downloading thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class Collected(Action<DownloadProgress>? onReport = null) : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = [];

        public void Report(DownloadProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    /// <summary>A response body that never sends another byte, like a connection that went quiet.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
