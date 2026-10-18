using PzLauncher.Core.Common;

namespace PzLauncher.Core.Net;

/// <summary>The proxy every Core network feature goes through.</summary>
public interface INetworkService
{
    /// <summary>The settings in effect, normalized; <see cref="ProxySettings.Default"/> until <see cref="ApplyProxy"/> succeeds.</summary>
    ProxySettings Proxy { get; }

    /// <summary>Takes effect for the next request; running requests finish on their old route. On failure nothing changes.</summary>
    Result ApplyProxy(ProxySettings settings);

    /// <summary>One result per release site (GitHub, Gitee), in that order, through the proxy in effect.</summary>
    Task<IReadOnlyList<ConnectionTestResult>> TestConnectionAsync(CancellationToken ct = default);
}
