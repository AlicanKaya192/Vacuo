using System.Text;
using Vacuo.Core.Updates;

namespace Vacuo.Windows;

public enum DownloadOutcome
{
    /// <summary>The setup program is in place and passed <see cref="DownloadVerifier"/>.</summary>
    Ready,
    Cancelled,

    /// <summary>The connection failed or went quiet for longer than the stall limit.</summary>
    Offline,

    /// <summary>GitHub answered with an error, or the offer points outside Vacuo's releases.</summary>
    Failed,

    /// <summary>The file came, but not the one the release lists: size, program or checksum differ.</summary>
    Rejected,
}

public readonly record struct DownloadProgress(long Received, long Total);

public sealed record DownloadResult(DownloadOutcome Outcome, string? InstallerPath = null, string Detail = "");

/// <summary>
/// Downloads a release's setup program into <c>%LOCALAPPDATA%\Vacuo\updates</c> in 256 KB pieces, reporting
/// progress after each and stopping at the next one when cancelled. The file is written as
/// "&lt;name&gt;.partial" and takes its name only after <see cref="DownloadVerifier"/> accepts it. A
/// cancelled, broken or rejected download removes its own partial file: the only thing this class deletes
/// (vacuo-kurulum-guncelleme-plani, "Silinenler").
/// </summary>
public sealed class UpdateDownloader(HttpClient http, string folder, TimeSpan stallTimeout)
{
    private const int PieceBytes = 256 * 1024;
    private const int MaximumChecksumBytes = 4096;

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "updates");

    /// <summary>A connection that sends nothing for this long is treated as lost; HttpClient's own limit ends with the headers.</summary>
    public static TimeSpan DefaultStallTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <param name="choice">The full setup or a patch (K4), each with its own SHA-256 file.</param>
    public async Task<DownloadResult> DownloadAsync(SetupChoice choice, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        ReleaseAsset setup = choice.Setup;
        if (!IsVacuoRelease(setup) || !IsVacuoRelease(choice.Checksum))
        {
            return new DownloadResult(DownloadOutcome.Failed, Detail: "The offer points outside Vacuo's releases.");
        }

        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, setup.Name);
        string partial = target + ".partial";
        try
        {
            if (await ReadChecksumAsync(choice.Checksum.Url, cancellation) is not { } checksum)
            {
                return new DownloadResult(DownloadOutcome.Failed, Detail: "The checksum file could not be read.");
            }

            if (await SaveAsync(setup, partial, progress, cancellation) is { } failure)
            {
                RemovePartial(partial);
                return failure;
            }

            DownloadVerdict verdict = DownloadVerifier.Verify(partial, setup.Size, checksum, setup.Name);
            if (verdict != DownloadVerdict.Accepted)
            {
                RemovePartial(partial);
                return new DownloadResult(DownloadOutcome.Rejected, Detail: verdict.ToString());
            }

            File.Move(partial, target, overwrite: true);
            return new DownloadResult(DownloadOutcome.Ready, target);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            RemovePartial(partial);
            return new DownloadResult(DownloadOutcome.Cancelled);
        }
        catch (OperationCanceledException)
        {
            RemovePartial(partial);
            return new DownloadResult(DownloadOutcome.Offline, Detail: "The download stalled.");
        }
        catch (HttpRequestException exception)
        {
            RemovePartial(partial);
            return new DownloadResult(DownloadOutcome.Offline, Detail: exception.Message);
        }
        catch (IOException exception)
        {
            RemovePartial(partial);
            return new DownloadResult(DownloadOutcome.Failed, Detail: exception.Message);
        }
    }

    private static bool IsVacuoRelease(ReleaseAsset asset) =>
        asset.Url.StartsWith(ReleaseFeed.DownloadPrefix, StringComparison.Ordinal) && asset.Url.EndsWith("/" + asset.Name, StringComparison.Ordinal);

    private async Task<string?> ReadChecksumAsync(string url, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(stallTimeout);
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(limit.Token);
        byte[] buffer = new byte[MaximumChecksumBytes + 1];
        int length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, limit.Token);
        return length > MaximumChecksumBytes ? null : Encoding.ASCII.GetString(buffer, 0, length);
    }

    /// <summary>Writes the setup to <paramref name="partial"/>; gives a result only when it went wrong.</summary>
    private async Task<DownloadResult?> SaveAsync(ReleaseAsset installer, string partial, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(stallTimeout);
        using HttpResponseMessage response = await http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (!response.IsSuccessStatusCode)
        {
            return new DownloadResult(DownloadOutcome.Failed, Detail: $"HTTP {(int)response.StatusCode}");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] piece = new byte[PieceBytes];
        long received = 0;
        while (true)
        {
            stall.CancelAfter(stallTimeout);
            int read = await source.ReadAtLeastAsync(piece, piece.Length, throwOnEndOfStream: false, stall.Token);
            if (read == 0)
            {
                return null;
            }

            received += read;
            if (received > installer.Size)
            {
                return new DownloadResult(DownloadOutcome.Rejected, Detail: DownloadVerdict.WrongSize.ToString());
            }

            await destination.WriteAsync(piece.AsMemory(0, read), stall.Token);
            progress?.Report(new DownloadProgress(received, installer.Size));
            cancellation.ThrowIfCancellationRequested();
        }
    }

    private static void RemovePartial(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next download of the same file, which overwrites it.
        }
    }
}
