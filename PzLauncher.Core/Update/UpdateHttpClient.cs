using System.Net.Http.Headers;

namespace PzLauncher.Core.Update;

/// <summary>The one <see cref="HttpClient"/> Core uses; timeouts are per request, set by the caller.</summary>
internal sealed class UpdateHttpClient : IDisposable
{
    public UpdateHttpClient(HttpMessageHandler handler, SemVersion current)
    {
        Client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub rejects API requests without a User-Agent.
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(LauncherReleases.ProductName, current.ToString()));
    }

    public HttpClient Client { get; }

    public static UpdateHttpClient Create(SemVersion current) =>
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15),
            },
            current);

    public void Dispose() => Client.Dispose();
}
