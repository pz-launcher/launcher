using System.ComponentModel;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Update;

public sealed class UpdateServiceInstallTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly TempDirectory _install = new();

    public UpdateServiceInstallTests()
    {
        WriteFile(_install.Path, "PzLauncher.exe", "old exe");
        WriteFile(_install.Path, "libSkiaSharp.dll", "old dll");
        WriteFile(_install.Path, "user-notes.txt", "mine");
    }

    public void Dispose() => _install.Dispose();

    private string UpdateRoot => Path.Combine(_install.Path, ".update");

    [Fact]
    public async Task Install_ReplacesPackagedFiles_KeepsOthers()
    {
        var staged = await StageAsync(("PzLauncher.exe", "new exe"), ("libSkiaSharp.dll", "new dll"), ("Assets/new.dat", "new data"));

        ResultAssert.Ok(await Service().InstallAsync(staged, TestContext.Current.CancellationToken));

        Assert.Equal("new exe", ReadText(Path.Combine(_install.Path, "PzLauncher.exe")));
        Assert.Equal("new dll", ReadText(Path.Combine(_install.Path, "libSkiaSharp.dll")));
        Assert.Equal("new data", ReadText(Path.Combine(_install.Path, "Assets", "new.dat")));
        Assert.Equal("mine", ReadText(Path.Combine(_install.Path, "user-notes.txt")));
        var old = Assert.Single(Directory.EnumerateDirectories(Path.Combine(UpdateRoot, "old")));
        Assert.Equal("old exe", ReadText(Path.Combine(old, "PzLauncher.exe")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "staged")));
        Assert.Null(await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Install_FailsMidway_LeavesFolderExactlyAsBefore(int failingMove)
    {
        var staged = await StageAsync(("PzLauncher.exe", "new exe"), ("libSkiaSharp.dll", "new dll"), ("Assets/new.dat", "new data"));
        var before = Snapshot(_install.Path);
        var moves = 0;
        var service = UpdateTestKit.Service(_handler, _install.Path, moveFile: (source, target) =>
        {
            if (++moves == failingMove)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            File.Move(source, target);
        });

        ResultAssert.Failed(await service.InstallAsync(staged, TestContext.Current.CancellationToken), ErrorCode.UpdateInstallFailed);

        var after = Snapshot(_install.Path);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Directories, after.Directories);
        Assert.Equal(staged, await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Install_RollBackFails_KeepsTheMovedAsideFile()
    {
        var staged = await StageAsync(("PzLauncher.exe", "new exe"));
        var moves = 0;
        var service = UpdateTestKit.Service(_handler, _install.Path, moveFile: (source, target) =>
        {
            if (++moves >= 2)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            File.Move(source, target);
        });

        ResultAssert.Failed(await service.InstallAsync(staged, TestContext.Current.CancellationToken), ErrorCode.UpdateInstallFailed);

        var old = Assert.Single(Directory.EnumerateDirectories(Path.Combine(UpdateRoot, "old")));
        Assert.Equal("old exe", ReadText(Path.Combine(old, "PzLauncher.exe")));
    }

    [Fact]
    public async Task Install_NotStaged_IsPackageInvalid()
    {
        var staged = new StagedUpdate(V("0.3.0"), Path.Combine(UpdateRoot, "staged", "0.3.0"), new Uri("https://example.test/"));

        ResultAssert.Failed(await Service().InstallAsync(staged, TestContext.Current.CancellationToken), ErrorCode.UpdatePackageInvalid);

        Assert.Equal("old exe", ReadText(Path.Combine(_install.Path, "PzLauncher.exe")));
    }

    [Fact]
    public async Task Install_DevelopmentBuild_Fails()
    {
        var staged = await StageAsync(("PzLauncher.exe", "new exe"));

        ResultAssert.Failed(
            await UpdateTestKit.Service(_handler, _install.Path, current: "0.0.0-dev").InstallAsync(staged, TestContext.Current.CancellationToken),
            ErrorCode.UpdateDevelopmentBuild);

        Assert.Equal("old exe", ReadText(Path.Combine(_install.Path, "PzLauncher.exe")));
    }

    [Fact]
    public async Task CleanUp_RemovesLeftovers_KeepsNewerStagedUpdate()
    {
        var staged = await StageAsync(("PzLauncher.exe", "0.3.0"));
        WriteFile(UpdateRoot, "old/20260101T000000000/PzLauncher.exe", "older exe");
        WriteFile(UpdateRoot, "download/PzLauncher-0.4.0-win-x64.zip.part", "partial");
        WriteFile(UpdateRoot, "staged/0.1.0/PzLauncher.exe", "stale");
        WriteFile(UpdateRoot, "staged/0.1.0.ready", "{}");
        WriteFile(UpdateRoot, "staged/0.5.0/PzLauncher.exe", "interrupted extraction");

        await Service().CleanUpLeftoversAsync(TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(UpdateRoot, "old")));
        Assert.False(Directory.Exists(Path.Combine(UpdateRoot, "download")));
        Assert.Equal(["0.3.0", "0.3.0.ready"], Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "staged")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(staged, await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanUp_StagedVersionAlreadyRunning_IsRemoved()
    {
        await StageAsync(("PzLauncher.exe", "0.3.0"));

        await UpdateTestKit.Service(_handler, _install.Path, current: "0.3.0").CleanUpLeftoversAsync(TestContext.Current.CancellationToken);

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(UpdateRoot, "staged")));
    }

    [Fact]
    public async Task CleanUp_NoUpdateFolder_DoesNothing()
    {
        await Service().CleanUpLeftoversAsync(TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(UpdateRoot));
    }

    [Fact]
    public async Task GetStaged_NotNewerThanRunning_IsNull()
    {
        await StageAsync(("PzLauncher.exe", "0.3.0"));

        Assert.Null(await UpdateTestKit.Service(_handler, _install.Path, current: "0.3.0").GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"releasePageUrl":"https://example.test/"}""")]
    [InlineData("""{"release_page_url":"not a url"}""")]
    public async Task GetStaged_UnreadableMarker_IsNull(string marker)
    {
        await StageAsync(("PzLauncher.exe", "0.3.0"));
        WriteFile(UpdateRoot, "staged/0.3.0.ready", marker);

        Assert.Null(await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStaged_MarkerWithoutFolder_IsNull()
    {
        WriteFile(UpdateRoot, "staged/0.3.0.ready", """{"release_page_url":"https://example.test/"}""");

        Assert.Null(await Service().GetStagedAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PlatformKind.Windows, "PzLauncher.exe")]
    [InlineData(PlatformKind.Linux, "PzLauncher")]
    public async Task StartInstalled_StartsTheLauncherInTheInstallFolder(PlatformKind os, string executable)
    {
        var processes = new FakeProcessRunner();

        ResultAssert.Ok(await UpdateTestKit.Service(_handler, _install.Path, os: os, processes: processes).StartInstalledAsync(TestContext.Current.CancellationToken));

        var request = processes.LastRequest;
        Assert.Equal(Path.Combine(_install.Path, executable), request.FileName);
        Assert.Equal(_install.Path, request.WorkingDirectory);
        Assert.Empty(request.Arguments);
        Assert.True(Assert.Single(processes.Processes).IsDisposed);
    }

    [Fact]
    public async Task StartInstalled_StartFails_IsRestartFailed()
    {
        var processes = new FakeProcessRunner { StartException = new Win32Exception(2, "The system cannot find the file specified.") };

        var error = ResultAssert.Failed(
            await UpdateTestKit.Service(_handler, _install.Path, processes: processes).StartInstalledAsync(TestContext.Current.CancellationToken),
            ErrorCode.UpdateRestartFailed);

        Assert.Equal(Path.Combine(_install.Path, "PzLauncher.exe"), error.Details["path"]);
    }

    private UpdateService Service() => UpdateTestKit.Service(_handler, _install.Path);

    private async Task<StagedUpdate> StageAsync(params (string Name, string Content)[] files)
    {
        var package = Zip(files);
        _handler.RespondBytes(PackageUrl, package);
        return ResultAssert.Ok(await Service().DownloadAsync(Release(package), ct: TestContext.Current.CancellationToken));
    }
}
