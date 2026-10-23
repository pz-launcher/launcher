using System.Net;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Update.Releases;

public sealed class ReleaseFeedTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly FakeHttpHandler _handler = new();
    private readonly LauncherHttpClient _http;

    public ReleaseFeedTests() => _http = new LauncherHttpClient(_handler, V("0.2.0"));

    private ReleaseFeed GitHub => new(LauncherReleases.GitHub, _http.Client);

    private ReleaseFeed Gitee => new(LauncherReleases.Gitee, _http.Client);

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task GitHub_RealResponse_PicksWindowsZipWithSizeAndDigest()
    {
        _handler.RespondJson(GitHubLatest, GitHubJson());

        var release = ResultAssert.Ok(await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken)).Release;

        Assert.NotNull(release);
        Assert.Equal(
            (V("0.2.0"), UpdateSource.GitHub, "PzLauncher-0.2.0-win-x64.zip", 20519474L, "633b18a3328dd15ff4cf611ec7593635c07af9b877cd731b4ae51abb004ede08"),
            (release.Version, release.Source, release.PackageName, release.Size, release.Sha256));
        Assert.Equal("https://github.com/pz-launcher/launcher/releases/download/v0.2.0/PzLauncher-0.2.0-win-x64.zip", release.PackageUrl.AbsoluteUri);
        Assert.Equal("https://github.com/pz-launcher/launcher/releases/tag/v0.2.0", release.ReleasePageUrl.AbsoluteUri);
    }

    [Fact]
    public async Task GitHub_LinuxRid_PicksTarGz()
    {
        _handler.RespondJson(GitHubLatest, GitHubJson());

        var release = ResultAssert.Ok(await GitHub.GetLatestAsync("linux-x64", Timeout, TestContext.Current.CancellationToken)).Release;

        Assert.Equal("PzLauncher-0.2.0-linux-x64.tar.gz", release?.PackageName);
        Assert.Equal("1425dbcbc60561117627ea4e7793ee4413bce6855cab4c8f6e13e6f5372d939a", release?.Sha256);
    }

    [Fact]
    public async Task Gitee_RealResponse_PicksPackageWithoutSizeOrDigest()
    {
        _handler.RespondJson(GiteeLatest, GiteeJson());

        var release = ResultAssert.Ok(await Gitee.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken)).Release;

        Assert.NotNull(release);
        Assert.Equal((UpdateSource.Gitee, "PzLauncher-0.2.0-win-x64.zip", (long?)null, (string?)null), (release.Source, release.PackageName, release.Size, release.Sha256));
        Assert.Equal("https://gitee.com/dontcampy/pzl/releases/download/v0.2.0/PzLauncher-0.2.0-win-x64.zip", release.PackageUrl.AbsoluteUri);
        Assert.Equal("https://gitee.com/dontcampy/pzl/releases/tag/v0.2.0", release.ReleasePageUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Gitee_OnlySourceArchives_IsNoRelease()
    {
        _handler.RespondJson(GiteeLatest, """
            {"tag_name":"v0.3.0","prerelease":false,"assets":[
              {"browser_download_url":"https://gitee.com/dontcampy/pzl/archive/refs/tags/v0.3.0.zip","name":"v0.3.0.zip"},
              {"browser_download_url":"https://gitee.com/dontcampy/pzl/archive/refs/tags/v0.3.0.tar.gz","name":"v0.3.0.tar.gz"}]}
            """);

        Assert.Null(ResultAssert.Ok(await Gitee.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken)).Release);
    }

    [Fact]
    public async Task UnknownRid_IsNoRelease()
    {
        _handler.RespondJson(GitHubLatest, GitHubJson());

        Assert.Null(ResultAssert.Ok(await GitHub.GetLatestAsync("osx-arm64", Timeout, TestContext.Current.CancellationToken)).Release);
    }

    [Theory]
    [InlineData("v0.3.0", true)]
    [InlineData("v0.3.0-beta.1", false)]
    public async Task Prerelease_IsNoRelease(string tag, bool prereleaseFlag)
    {
        _handler.RespondJson(GitHubLatest, GitHubJson().Replace("\"v0.2.0\"", $"\"{tag}\"", StringComparison.Ordinal)
            .Replace("\"prerelease\": false", $"\"prerelease\": {(prereleaseFlag ? "true" : "false")}", StringComparison.Ordinal));

        Assert.Null(ResultAssert.Ok(await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken)).Release);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"prerelease":false,"assets":[]}""")]
    [InlineData("""{"tag_name":null,"prerelease":false,"assets":[]}""")]
    [InlineData("""{"tag_name":"latest","prerelease":false,"assets":[]}""")]
    [InlineData("""{"tag_name":"v0.3.0","prerelease":false,"assets":[{"name":"PzLauncher-0.3.0-win-x64.zip","browser_download_url":"http://example.test/p.zip"}]}""")]
    [InlineData("""{"tag_name":"v0.3.0","prerelease":false,"assets":[{"name":"PzLauncher-0.3.0-win-x64.zip","browser_download_url":"https://example.test/p.zip","digest":"sha256:xyz"}]}""")]
    [InlineData("null")]
    public async Task MalformedBody_IsReleaseInfoInvalid(string body)
    {
        _handler.RespondJson(GitHubLatest, body);

        ResultAssert.Failed(await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken), ErrorCode.UpdateReleaseInfoInvalid);
    }

    [Fact]
    public async Task DigestOfAnotherAlgorithm_IsIgnored()
    {
        _handler.RespondJson(GitHubLatest, """
            {"tag_name":"v0.3.0","prerelease":false,"assets":[{"name":"PzLauncher-0.3.0-win-x64.zip","browser_download_url":"https://example.test/p.zip","digest":"sha512:abc"}]}
            """);

        var release = ResultAssert.Ok(await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken)).Release;

        Assert.NotNull(release);
        Assert.Null(release.Sha256);
    }

    [Fact]
    public async Task HttpError_IsNetworkFailed()
    {
        _handler.RespondStatus(GitHubLatest, HttpStatusCode.Forbidden);

        var error = ResultAssert.Failed(await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);

        Assert.Equal("GitHub", error.Details["source"]);
    }

    [Fact]
    public async Task ConnectionFailure_IsNetworkFailed()
    {
        _handler.Throw(GiteeLatest);

        ResultAssert.Failed(await Gitee.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);
    }

    [Fact]
    public async Task NoAnswerWithinTimeout_IsNetworkFailed()
    {
        _handler.Hang(GitHubLatest);

        ResultAssert.Failed(await GitHub.GetLatestAsync("win-x64", TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        _handler.Hang(GitHubLatest);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GitHub.GetLatestAsync("win-x64", Timeout, cts.Token));
    }

    [Fact]
    public async Task Request_SendsUserAgentAndAcceptsJson()
    {
        _handler.RespondJson(GitHubLatest, GitHubJson());

        await GitHub.GetLatestAsync("win-x64", Timeout, TestContext.Current.CancellationToken);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("PzLauncher/0.2.0", request.UserAgent);
        Assert.Equal("application/json", request.Accept);
    }
}
