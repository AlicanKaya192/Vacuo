using System.Net.Http.Headers;

namespace Vacuo.Windows;

/// <summary>
/// The one HTTP client Vacuo uses, for update checks and downloads only (vacuo-kurallar 6). GitHub refuses
/// requests without a User-Agent; "Vacuo/&lt;version&gt;" is the only thing about this computer it learns.
/// </summary>
public static class GitHubHttp
{
    public static HttpClient Create(string appVersion) =>
        Create(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }, appVersion);

    public static HttpClient Create(HttpMessageHandler handler, string appVersion)
    {
        var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vacuo", appVersion));
        return http;
    }
}
