using System.Net.Http.Headers;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Net;

/// <summary>The one <see cref="HttpClient"/> Core uses; timeouts are per request, set by the caller.</summary>
internal sealed class LauncherHttpClient : IDisposable
{
    public LauncherHttpClient(HttpMessageHandler handler, SemVersion current)
    {
        Client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub rejects API requests without a User-Agent.
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(LauncherReleases.ProductName, current.ToString()));
    }

    public HttpClient Client { get; }

    // The handler reads Proxy once, at its first request; switching happens inside SwitchableProxy.
    public static LauncherHttpClient Create(SemVersion current, SwitchableProxy proxy) =>
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15),
                UseProxy = true,
                Proxy = proxy,
            },
            current);

    public void Dispose() => Client.Dispose();
}
