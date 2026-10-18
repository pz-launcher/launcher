using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Net;

public sealed class NetworkServiceTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly LauncherHttpClient _http;
    private readonly SwitchableProxy _proxy = new(new WebProxy("http://system-proxy.example.test:8080"));

    public NetworkServiceTests() => _http = new LauncherHttpClient(_handler, V("0.2.0"));

    public void Dispose() => _http.Dispose();

    private NetworkService CreateService(TimeSpan? timeout = null) =>
        new(_proxy, _http, TimeProvider.System, NullLogger<NetworkService>.Instance) { TestTimeout = timeout ?? TimeSpan.FromSeconds(30) };

    [Fact]
    public void Proxy_BeforeAnyApply_IsSystem() => Assert.Equal(ProxySettings.Default, CreateService().Proxy);

    [Fact]
    public void ApplyProxy_Valid_StoresNormalizedSettingsAndSwitchesTheRoute()
    {
        var service = CreateService();

        ResultAssert.Ok(service.ApplyProxy(new ProxySettings(ProxyMode.Manual, new ManualProxy("http://127.0.0.1", 7890, "", "pw"))));

        Assert.Equal(new ProxySettings(ProxyMode.Manual, new ManualProxy("127.0.0.1", 7890, null, null)), service.Proxy);
        Assert.Equal(new Uri("http://127.0.0.1:7890/"), _proxy.GetProxy(new Uri(GitHubLatest)));
    }

    [Fact]
    public void ApplyProxy_Invalid_FailsAndKeepsTheSettingsInEffect()
    {
        var service = CreateService();
        ResultAssert.Ok(service.ApplyProxy(new ProxySettings(ProxyMode.None, null)));

        ResultAssert.Failed(service.ApplyProxy(new ProxySettings(ProxyMode.Manual, new ManualProxy("127.0.0.1", 0, null, null))), ErrorCode.NetworkProxyPortInvalid);

        Assert.Equal(new ProxySettings(ProxyMode.None, null), service.Proxy);
        Assert.True(_proxy.IsBypassed(new Uri(GitHubLatest)));
    }

    [Fact]
    public async Task TestConnectionAsync_BothAnswer_ReportsBothReachableInOrder()
    {
        _handler.RespondStatus(GitHubLatest, HttpStatusCode.OK);
        // A rate limit still proves the route works.
        _handler.RespondStatus(GiteeLatest, HttpStatusCode.Forbidden);

        var results = await CreateService().TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["GitHub", "Gitee"], results.Select(result => result.Name));
        Assert.Equal([new Uri(GitHubLatest), new Uri(GiteeLatest)], results.Select(result => result.Url));
        Assert.All(results, result => Assert.Null(result.Error));
        Assert.All(_handler.Requests, request => Assert.StartsWith("PzLauncher/0.2.0", request.UserAgent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestConnectionAsync_OneFails_ReportsItUnreachableAndTheOtherReachable()
    {
        _handler.RespondStatus(GitHubLatest, HttpStatusCode.OK);
        _handler.Throw(GiteeLatest);

        var results = await CreateService().TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Null(results[0].Error);
        var error = results[1].Error;
        Assert.Equal(ErrorCode.NetworkUnreachable, error?.Code);
        Assert.Equal("Gitee", error?.Details["target"]);
        Assert.Equal("Connection refused", error?.Details["reason"]);
    }

    [Fact]
    public async Task TestConnectionAsync_BothFail_ReportsBothUnreachable()
    {
        _handler.Throw(GitHubLatest);
        _handler.Throw(GiteeLatest);

        var results = await CreateService().TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal([ErrorCode.NetworkUnreachable, ErrorCode.NetworkUnreachable], results.Select(result => result.Error?.Code));
        Assert.Equal(["GitHub", "Gitee"], results.Select(result => result.Error?.Details["target"]));
    }

    [Fact]
    public async Task TestConnectionAsync_NoAnswerInTime_ReportsTimeout()
    {
        _handler.Hang(GitHubLatest);
        _handler.RespondStatus(GiteeLatest, HttpStatusCode.OK);

        var results = await CreateService(TimeSpan.FromMilliseconds(50)).TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.NetworkTimeout, results[0].Error?.Code);
        Assert.Equal("GitHub", results[0].Error?.Details["target"]);
        Assert.Null(results[1].Error);
    }

    [Fact]
    public async Task TestConnectionAsync_RunsBothSitesAtOnce()
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond = async (_, ct) =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        _handler.Respond(GitHubLatest, respond);
        _handler.Respond(GiteeLatest, respond);

        var results = await CreateService().TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.All(results, result => Assert.Null(result.Error));
    }

    [Fact]
    public async Task TestConnectionAsync_Cancelled_Throws()
    {
        _handler.Hang(GitHubLatest);
        _handler.Hang(GiteeLatest);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var test = CreateService().TestConnectionAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
    }
}
