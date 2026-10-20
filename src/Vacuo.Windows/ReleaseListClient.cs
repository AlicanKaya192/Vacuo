using System.Net.Http.Headers;
using System.Text;
using Vacuo.Core.Updates;

namespace Vacuo.Windows;

/// <summary>
/// Asks GitHub for Vacuo's release list: one GET without a body or an account, a short time limit, and an
/// answer of bounded size. It never throws for the network: no internet is <see cref="UpdateStatus.Offline"/>,
/// an error answer (404 while the repository is private, 403 at the rate limit) is <see cref="UpdateStatus.Failed"/>.
/// Only the caller's own cancellation is thrown.
/// </summary>
public sealed class ReleaseListClient(HttpClient http, TimeSpan timeout)
{
    public const string ListUrl = "https://api.github.com/repos/AlicanKaya192/vacuo/releases?per_page=30";

    // Release notes make the list long: Odyssey measured 103 KB for 15 releases.
    public const int MaximumAnswerBytes = 2 * 1024 * 1024;

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(6);

    public async Task<UpdateCheck> CheckAsync(ReleaseVersion current, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ListUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheck.Failed($"HTTP {(int)response.StatusCode}");
            }

            string? json = await ReadBoundedAsync(response.Content, limit.Token);
            return json is null ? UpdateCheck.Failed("The release list is too long.") : ReleaseFeed.Read(json, current);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return UpdateCheck.Offline("No answer in time.");
        }
        catch (HttpRequestException exception)
        {
            return UpdateCheck.Offline(exception.Message);
        }
    }

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellation)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        byte[] piece = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(piece, cancellation)) > 0)
        {
            if (buffer.Length + read > MaximumAnswerBytes)
            {
                return null;
            }

            buffer.Write(piece, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
