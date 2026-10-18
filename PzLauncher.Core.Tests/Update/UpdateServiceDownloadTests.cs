using System.Net;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Update;

public sealed class UpdateServiceDownloadTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly TempDirectory _install = new();

    public void Dispose() => _install.Dispose();

    private string UpdateRoot => Path.Combine(_install.Path, ".update");

    [Fact]
    public async Task Zip_VerifiedAndStaged_WithProgress()
    {
        var package = Zip(("PzLauncher.exe", "new exe"), ("lib/native.dll", "new dll"));
        _handler.RespondBytes(PackageUrl, package);
        var progress = new ListProgress<DownloadProgress>();
        var service = Service();

        var staged = ResultAssert.Ok(await service.DownloadAsync(Release(package), progress, TestContext.Current.CancellationToken));

        Assert.Equal(V("0.3.0"), staged.Version);
        Assert.Equal(Path.Combine(UpdateRoot, "staged", "0.3.0", "PzLauncher"), staged.StagingDirectory);
        Assert.Equal("new exe", ReadText(Path.Combine(staged.StagingDirectory, "PzLauncher.exe")));
        Assert.Equal("new dll", ReadText(Path.Combine(staged.StagingDirectory, "lib", "native.dll")));
        Assert.True(File.Exists(Path.Combine(UpdateRoot, "staged", "0.3.0.ready")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "download")));
        Assert.Equal(new DownloadProgress(package.Length, package.Length), progress.Reports[^1]);
        Assert.Equal(staged, await service.GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TarGz_FromGiteeWithoutChecksum_StagedOnLinux()
    {
        var package = TarGz(("PzLauncher", "new elf"), ("libSkiaSharp.so", "so"));
        _handler.RespondBytes(PackageUrl, package);

        var staged = ResultAssert.Ok(await Service(PlatformKind.Linux).DownloadAsync(
            Release(package, rid: "linux-x64", withChecksum: false), ct: TestContext.Current.CancellationToken));

        Assert.Equal("new elf", ReadText(Path.Combine(staged.StagingDirectory, "PzLauncher")));
        Assert.True(File.Exists(Path.Combine(staged.StagingDirectory, "libSkiaSharp.so")));
    }

    [Fact]
    public async Task ChecksumMismatch_DiscardedAsPackageInvalid()
    {
        var package = Zip(("PzLauncher.exe", "new exe"));
        var tampered = Zip(("PzLauncher.exe", "evil exe"));
        _handler.RespondBytes(PackageUrl, tampered);
        var release = Release(package) with { Size = tampered.Length };

        ResultAssert.Failed(await Service().DownloadAsync(release, ct: TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
    }

    [Fact]
    public async Task SizeMismatch_DiscardedAsPackageInvalid()
    {
        var package = Zip(("PzLauncher.exe", "new exe"));
        _handler.RespondBytes(PackageUrl, package);

        ResultAssert.Failed(
            await Service().DownloadAsync(Release(package) with { Size = package.Length + 1 }, ct: TestContext.Current.CancellationToken),
            ErrorCode.UpdatePackageInvalid);
        ResultAssert.Failed(
            await Service().DownloadAsync(Release(package) with { Size = package.Length - 1 }, ct: TestContext.Current.CancellationToken),
            ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
    }

    [Fact]
    public async Task ArchiveWithoutExecutable_DiscardedAsPackageInvalid()
    {
        var package = Zip(("README.txt", "no launcher here"), ("sub/PzLauncher.exe", "not at the folder root"));
        _handler.RespondBytes(PackageUrl, package);

        ResultAssert.Failed(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
    }

    [Fact]
    public async Task ZipEntryEscapingFolder_DiscardedAsPackageInvalid()
    {
        var package = Zip(("PzLauncher.exe", "new exe"), ("../../escaped.txt", "evil"));
        _handler.RespondBytes(PackageUrl, package);

        ResultAssert.Failed(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
        Assert.False(File.Exists(Path.Combine(UpdateRoot, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(UpdateRoot, "staged", "escaped.txt")));
    }

    [Fact]
    public async Task TarEntryEscapingFolder_DiscardedAsPackageInvalid()
    {
        var package = TarGz(("PzLauncher", "new elf"), ("../../escaped", "evil"));
        _handler.RespondBytes(PackageUrl, package);

        ResultAssert.Failed(
            await Service(PlatformKind.Linux).DownloadAsync(Release(package, rid: "linux-x64"), ct: TestContext.Current.CancellationToken),
            ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
        Assert.False(File.Exists(Path.Combine(UpdateRoot, "escaped")));
    }

    [Fact]
    public async Task CorruptArchive_DiscardedAsPackageInvalid()
    {
        byte[] package = [.. "PK\u0003\u0004 not really a zip"u8];
        _handler.RespondBytes(PackageUrl, package);

        ResultAssert.Failed(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        AssertNothingStaged();
    }

    [Fact]
    public async Task HttpError_IsNetworkFailed()
    {
        _handler.RespondStatus(PackageUrl, HttpStatusCode.NotFound);

        ResultAssert.Failed(await Service().DownloadAsync(Release([1, 2, 3]), ct: TestContext.Current.CancellationToken), ErrorCode.UpdateNetworkFailed);

        AssertNothingStaged();
    }

    [Fact]
    public async Task StalledDownload_IsNetworkFailed_AndPartialFileRemoved()
    {
        var package = Zip(("PzLauncher.exe", "new exe"));
        _handler.RespondStream(PackageUrl, () => new StallingStream([.. package.Take(10)]));
        var service = UpdateTestKit.Service(_handler, _install.Path, timeout: TimeSpan.FromMilliseconds(100));

        var result = await service.DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken);

        ResultAssert.Failed(result, ErrorCode.UpdateNetworkFailed);
        AssertNothingStaged();
    }

    [Fact]
    public async Task Cancelled_Throws_AndPartialFileRemoved()
    {
        var package = new byte[300_000];
        Random.Shared.NextBytes(package);
        _handler.RespondBytes(PackageUrl, package);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new ListProgress<DownloadProgress>(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().DownloadAsync(Release(package), progress, cts.Token));

        AssertNothingStaged();
    }

    [Fact]
    public async Task AlreadyStaged_ReturnsItWithoutDownloading()
    {
        var package = Zip(("PzLauncher.exe", "new exe"));
        _handler.RespondBytes(PackageUrl, package);
        var first = ResultAssert.Ok(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken));

        var second = ResultAssert.Ok(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken));

        Assert.Equal(first, second);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task NewerDownload_ReplacesOlderStagedVersion()
    {
        var older = Zip(("PzLauncher.exe", "0.3.0"));
        _handler.RespondBytes(PackageUrl, older);
        ResultAssert.Ok(await Service().DownloadAsync(Release(older), ct: TestContext.Current.CancellationToken));
        var newer = Zip(("PzLauncher.exe", "0.4.0"));
        _handler.RespondBytes(PackageUrl, newer);

        var staged = ResultAssert.Ok(await Service().DownloadAsync(Release(newer, version: "0.4.0"), ct: TestContext.Current.CancellationToken));

        Assert.Equal(["0.4.0", "0.4.0.ready"], Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "staged")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(staged, await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewerDownloadInvalid_KeepsOlderStagedVersion()
    {
        var older = Zip(("PzLauncher.exe", "0.3.0"));
        _handler.RespondBytes(PackageUrl, older);
        var staged = ResultAssert.Ok(await Service().DownloadAsync(Release(older), ct: TestContext.Current.CancellationToken));
        var newer = Zip(("readme.txt", "no executable"));
        _handler.RespondBytes(PackageUrl, newer);

        ResultAssert.Failed(
            await Service().DownloadAsync(Release(newer, version: "0.4.0"), ct: TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        Assert.Equal(staged, await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../PzLauncher-0.3.0-win-x64.zip")]
    [InlineData("sub/PzLauncher-0.3.0-win-x64.zip")]
    [InlineData("..")]
    public async Task PackageNameWithPath_IsPackageInvalid(string name)
    {
        ResultAssert.Failed(
            await Service().DownloadAsync(Release([1]) with { PackageName = name }, ct: TestContext.Current.CancellationToken),
            ErrorCode.UpdatePackageInvalid);

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task DevelopmentBuild_FailsWithoutRequest()
    {
        var service = UpdateTestKit.Service(_handler, _install.Path, current: "0.0.0-dev");

        ResultAssert.Failed(await service.DownloadAsync(Release([1]), ct: TestContext.Current.CancellationToken), ErrorCode.UpdateDevelopmentBuild);

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task InstallFolderNotWritable_FailsWithoutRequest()
    {
        var notAFolder = Path.Combine(_install.Path, "file");
        WriteFile(_install.Path, "file", "");
        var service = UpdateTestKit.Service(_handler, notAFolder);

        Assert.False(service.CanInstallInPlace);
        ResultAssert.Failed(await service.DownloadAsync(Release([1]), ct: TestContext.Current.CancellationToken), ErrorCode.UpdateInstallFolderNotWritable);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public void CanInstallInPlace_WritableFolder_LeavesNoProbe()
    {
        Assert.True(Service().CanInstallInPlace);

        Assert.Empty(Directory.EnumerateFiles(UpdateRoot));
    }

    private UpdateService Service(PlatformKind os = PlatformKind.Windows) => UpdateTestKit.Service(_handler, _install.Path, os: os);

    private void AssertNothingStaged()
    {
        Assert.Empty(Directory.Exists(Path.Combine(UpdateRoot, "staged")) ? Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "staged")) : []);
        Assert.Empty(Directory.Exists(Path.Combine(UpdateRoot, "download")) ? Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "download")) : []);
    }
}
