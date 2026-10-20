using System.Net;

namespace Vacuo.Windows.Tests;

/// <summary>Answers HTTP requests from the test instead of the network, and keeps what was asked.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static StubHttpHandler Always(HttpStatusCode status, byte[] body) =>
        new((_, _) => Task.FromResult(Response(status, body)));

    public static HttpResponseMessage Response(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return answer(request, cancellationToken);
    }
}
