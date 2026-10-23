using System.Net;
using System.Runtime.InteropServices;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Update;

public sealed class UpdateServiceCheckTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly TempDirectory _install = new();

    public void Dispose() => _install.Dispose();

    [Theory]
    [InlineData("0.2.0", "0.3.0", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.3.0", "0.2.0", false)]
    [InlineData("0.2.0-beta.1", "0.2.0", true)]
    public async Task Check_ComparesWithLatestStable(string current, string latest, bool available)
    {
        _handler.RespondJson(GitHubLatest, GitHubJson(latest));

        var check = ResultAssert.Ok(await Service(current).CheckAsync(UpdateSource.GitHub, TestContext.Current.CancellationToken));

        Assert.Equal(V(current), check.Current);
        Assert.Equal(available, check.Available is not null);
        if (available)
        {
            Assert.Equal(V(latest), check.Available?.Version);
        }
    }

    [Fact]
    public async Task Check_NewerPrerelease_IsUpToDate()
    {
        _handler.RespondJson(GiteeLatest, GiteeJson("0.3.0-beta.1"));

        var check = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Gitee, TestContext.Current.CancellationToken));

        Assert.Null(check.Available);
    }

    [Fact]
    public async Task Check_NewerReleaseWithoutPlatformPackage_IsNoUpdate()
    {
        _handler.RespondJson(GiteeLatest, """
            {"tag_name":"v0.3.0","prerelease":false,"assets":[{"browser_download_url":"https://gitee.com/dontcampy/pzl/archive/refs/tags/v0.3.0.zip","name":"v0.3.0.zip"}]}
            """);

        Assert.Null(ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Gitee, TestContext.Current.CancellationToken)).Available);
    }

    [Fact]
    public async Task Auto_GiteeAnswersFirst_UsesGiteeAndCancelsGitHub()
    {
        _handler.Hang(GitHubLatest);
        _handler.RespondJson(GiteeLatest, GiteeJson("0.3.0"));

        var release = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available;

        Assert.NotNull(release);
        Assert.Equal(UpdateSource.Gitee, release.Source);
        Assert.Equal("gitee.com", release.PackageUrl.Host);
        Assert.Equal("gitee.com", release.ReleasePageUrl.Host);
        await WaitUntilAsync(() => _handler.Cancelled.Count == 1);
        Assert.Equal("api.github.com", Assert.Single(_handler.Cancelled).Host);
    }

    [Fact]
    public async Task Auto_GitHubAnswersFirst_UsesGitHub()
    {
        var giteeGate = new TaskCompletionSource();
        _handler.RespondJson(GitHubLatest, GitHubJson("0.3.0"));
        _handler.RespondJsonAfter(GiteeLatest, GiteeJson("0.3.0"), giteeGate.Task);

        var release = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available;

        Assert.Equal(UpdateSource.GitHub, release?.Source);
        Assert.Equal("github.com", release?.PackageUrl.Host);
    }

    [Fact]
    public async Task Auto_GiteeFails_UsesGitHub()
    {
        var gitHubGate = new TaskCompletionSource();
        _handler.RespondJsonAfter(GitHubLatest, GitHubJson("0.3.0"), gitHubGate.Task);
        _handler.Respond(GiteeLatest, (_, _) =>
        {
            gitHubGate.SetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        });

        var release = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available;

        Assert.Equal(UpdateSource.GitHub, release?.Source);
    }

    [Fact]
    public async Task Auto_GiteeAnswersUpToDate_WinsOverSlowerGitHub()
    {
        _handler.Hang(GitHubLatest);
        _handler.RespondJson(GiteeLatest, GiteeJson("0.2.0"));

        var check = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken));

        Assert.Null(check.Available);
    }

    [Fact]
    public async Task Auto_GiteeAnswersFirstWithoutPackage_WaitsForGitHub()
    {
        var gitHubGate = new TaskCompletionSource();
        _handler.RespondJsonAfter(GitHubLatest, GitHubJson("0.3.0"), gitHubGate.Task);
        _handler.Respond(GiteeLatest, (_, _) =>
        {
            gitHubGate.SetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v0.3.0","prerelease":false,"assets":[]}"""),
            });
        });

        var release = ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available;

        Assert.Equal(UpdateSource.GitHub, release?.Source);
    }

    [Fact]
    public async Task Auto_BothWithoutPackage_IsNoUpdate()
    {
        _handler.RespondJson(GitHubLatest, """{"tag_name":"v0.3.0","prerelease":false,"assets":[]}""");
        _handler.RespondJson(GiteeLatest, """{"tag_name":"v0.3.0","prerelease":false,"assets":[]}""");

        Assert.Null(ResultAssert.Ok(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available);
    }

    [Fact]
    public async Task Auto_BothFail_ReturnsGitHubNetworkError()
    {
        _handler.Throw(GitHubLatest);
        _handler.RespondStatus(GiteeLatest, HttpStatusCode.ServiceUnavailable);

        var error = ResultAssert.Failed(await Service().CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);

        Assert.Equal("GitHub", error.Details["source"]);
    }

    [Theory]
    [InlineData(UpdateSource.GitHub, "api.github.com")]
    [InlineData(UpdateSource.Gitee, "gitee.com")]
    public async Task PinnedSource_ContactsOnlyThatSource(UpdateSource source, string host)
    {
        _handler.RespondJson(GitHubLatest, GitHubJson("0.3.0"));
        _handler.RespondJson(GiteeLatest, GiteeJson("0.3.0"));

        var release = ResultAssert.Ok(await Service().CheckAsync(source, TestContext.Current.CancellationToken)).Available;

        Assert.Equal(source, release?.Source);
        Assert.Equal(host, Assert.Single(_handler.RequestedHosts));
    }

    [Fact]
    public async Task PinnedSource_Fails_DoesNotFallBack()
    {
        _handler.Throw(GiteeLatest);
        _handler.RespondJson(GitHubLatest, GitHubJson("0.3.0"));

        ResultAssert.Failed(await Service().CheckAsync(UpdateSource.Gitee, TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);
        Assert.Equal("gitee.com", Assert.Single(_handler.RequestedHosts));
    }

    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("0.0.0-ci.17")]
    public async Task DevelopmentBuild_FailsWithoutRequest(string current)
    {
        var service = Service(current);

        Assert.True(service.IsDevelopmentBuild);
        ResultAssert.Failed(await service.CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken), ErrorCode.UpdateDevelopmentBuild);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UnpublishedPlatform_IsUpToDateWithoutRequest()
    {
        var check = ResultAssert.Ok(await Service(os: PlatformKind.MacOS).CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken));

        Assert.Null(check.Available);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UnpublishedArchitecture_IsUpToDateWithoutRequest()
    {
        var service = UpdateTestKit.Service(_handler, _install.Path, architecture: Architecture.Arm64);

        Assert.Null(ResultAssert.Ok(await service.CheckAsync(UpdateSource.Auto, TestContext.Current.CancellationToken)).Available);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Linux_ChecksForTarGz()
    {
        _handler.RespondJson(GitHubLatest, GitHubJson("0.3.0"));

        var release = ResultAssert.Ok(await Service(os: PlatformKind.Linux).CheckAsync(UpdateSource.GitHub, TestContext.Current.CancellationToken)).Available;

        Assert.Equal("PzLauncher-0.3.0-linux-x64.tar.gz", release?.PackageName);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        _handler.Hang(GitHubLatest);
        _handler.Hang(GiteeLatest);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().CheckAsync(UpdateSource.Auto, cts.Token));
    }

    private UpdateService Service(string current = "0.2.0", PlatformKind os = PlatformKind.Windows) =>
        UpdateTestKit.Service(_handler, _install.Path, current, os);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
