using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Net;

internal sealed partial class NetworkService(
    SwitchableProxy proxy,
    LauncherHttpClient http,
    TimeProvider time,
    ILogger<NetworkService> logger) : INetworkService
{
    private static readonly ConnectionTestTarget[] Targets =
    [
        new("GitHub", LauncherReleases.GitHub.LatestReleaseUrl),
        new("Gitee", LauncherReleases.Gitee.LatestReleaseUrl),
    ];

    // Settings and the proxy's route change together.
    private readonly Lock _gate = new();
    private ProxySettings _settings = ProxySettings.Default;

    public ProxySettings Proxy
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    internal TimeSpan TestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public Result ApplyProxy(ProxySettings settings)
    {
        var validated = ProxySettingsValidation.Validate(settings);
        if (!validated.IsSuccess)
        {
            return validated.Error;
        }

        lock (_gate)
        {
            proxy.Use(validated.Value);
            _settings = validated.Value;
        }

        LogProxyApplied(logger, validated.Value.Mode);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<ConnectionTestResult>> TestConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Task.WhenAll(Targets.Select(target => TestAsync(target, ct))).ConfigureAwait(false);
    }

    private async Task<ConnectionTestResult> TestAsync(ConnectionTestTarget target, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TestTimeout);
        var started = time.GetTimestamp();
        Error? error;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            // Any status proves the route works: a 403 rate limit still came back through the proxy.
            using var response = await http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            error = null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            error = NetworkErrors.Timeout(target.Name, TestTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            error = NetworkErrors.Unreachable(target.Name, ex.Message);
        }

        if (error is not null)
        {
            LogTestFailed(logger, target.Name, error.Code.ToWireName(), error.Message);
        }

        return new ConnectionTestResult(target.Name, target.Url, time.GetElapsedTime(started), error);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proxy mode set to {Mode}")]
    private static partial void LogProxyApplied(ILogger logger, ProxyMode mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection test to {Target} failed: {Code} {Message}")]
    private static partial void LogTestFailed(ILogger logger, string target, string code, string message);

    private sealed record ConnectionTestTarget(string Name, Uri Url);
}
