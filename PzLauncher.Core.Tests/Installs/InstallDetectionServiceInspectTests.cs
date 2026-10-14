using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Installs;

public sealed class InstallDetectionServiceInspectTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly InstallDetectionService _service =
        InstallDetectionServiceScanTests.CreateService(FakePlatformEnvironment.WithRegistrySteam(null));

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InspectAsync_NullPath_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.InspectAsync(null!, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/folder")]
    [InlineData("bad\0path")]
    public async Task InspectAsync_InvalidPath_ReturnsInvalidPath(string path)
    {
        var result = await _service.InspectAsync(path, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InstallInvalidPath, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public async Task InspectAsync_DirectoryMissing_ReturnsPathNotFound()
    {
        var missing = Path.Combine(_temp.Path, "nope");

        var result = await _service.InspectAsync(missing, Ct);

        Assert.Equal(ErrorCode.InstallPathNotFound, result.Error!.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(missing, result.Error.Details["path"]);
    }

    [Fact]
    public async Task InspectAsync_DirectoryNotListable_ReturnsPathUnreadable()
    {
        var dir = _temp.CreateDirectory("locked");
        File.WriteAllText(Path.Combine(dir, "StartServer64.bat"), "");

        if (OperatingSystem.IsWindows())
        {
            var result = await WithDeniedListingAsync(dir, () => _service.InspectAsync(dir, Ct));

            Assert.Equal(ErrorCode.InstallPathUnreadable, result.Error!.Code);
            Assert.Equal(ErrorKind.External, result.Error.Kind);
        }
        else
        {
            File.SetUnixFileMode(dir, UnixFileMode.None);
            try
            {
                Assert.SkipWhen(CanList(dir), "Running as a user that ignores permissions (root).");
                var result = await _service.InspectAsync(dir, Ct);

                Assert.Equal(ErrorCode.InstallPathUnreadable, result.Error!.Code);
                Assert.Equal(ErrorKind.External, result.Error.Kind);
            }
            finally
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Fact]
    public async Task InspectAsync_EmptyDirectory_ReturnsNotAPzInstall()
    {
        var dir = _temp.CreateDirectory("empty");

        var result = await _service.InspectAsync(dir, Ct);

        Assert.Equal(ErrorCode.InstallNotAPzInstall, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Theory]
    [InlineData("StartServer64.bat")]
    [InlineData("start-server.sh")]
    public async Task InspectAsync_ServerMarker_ReturnsManualDedicatedServer(string marker)
    {
        var dir = _temp.CreateDirectory("server");
        File.WriteAllText(Path.Combine(dir, marker), "");

        var result = await _service.InspectAsync(dir + Path.DirectorySeparatorChar, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PzInstall(PzInstallKind.DedicatedServer, dir, Steam: null), result.Value);
    }

    [Fact]
    public async Task InspectAsync_ServerAndGameMarkers_PrefersServer()
    {
        var dir = _temp.CreateDirectory("both");
        File.WriteAllText(Path.Combine(dir, "ProjectZomboid64.exe"), "");
        File.WriteAllText(Path.Combine(dir, "StartServer64.bat"), "");

        var result = await _service.InspectAsync(dir, Ct);

        Assert.Equal(PzInstallKind.DedicatedServer, result.Value!.Kind);
    }

    [Theory]
    [InlineData("ProjectZomboid64.exe", false)]
    [InlineData("ProjectZomboid64", false)]
    [InlineData("projectzomboid.sh", false)]
    [InlineData("Project Zomboid.app", true)]
    public async Task InspectAsync_GameMarker_ReturnsManualGame(string marker, bool isDirectory)
    {
        var dir = _temp.CreateDirectory("game");
        if (isDirectory)
        {
            Directory.CreateDirectory(Path.Combine(dir, marker));
        }
        else
        {
            File.WriteAllText(Path.Combine(dir, marker), "");
        }

        var result = await _service.InspectAsync(dir, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PzInstall(PzInstallKind.Game, dir, Steam: null), result.Value);
    }

    [Fact]
    public async Task InspectAsync_GameShipsServerBat_StillGame()
    {
        // The real game folder contains ProjectZomboidServer.bat (see research/install-markers.md).
        var dir = _temp.CreateDirectory("game");
        File.WriteAllText(Path.Combine(dir, "ProjectZomboid64.exe"), "");
        File.WriteAllText(Path.Combine(dir, "ProjectZomboidServer.bat"), "");

        var result = await _service.InspectAsync(dir, Ct);

        Assert.Equal(PzInstallKind.Game, result.Value!.Kind);
    }

    [Fact]
    public async Task InspectAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.InspectAsync(_temp.Path, cts.Token));
    }

    [SupportedOSPlatform("windows")]
    private static async Task<T> WithDeniedListingAsync<T>(string dir, Func<Task<T>> action)
    {
        var info = new DirectoryInfo(dir);
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        try
        {
            return await action();
        }
        finally
        {
            acl = info.GetAccessControl();
            acl.RemoveAccessRule(rule);
            info.SetAccessControl(acl);
        }
    }

    private static bool CanList(string dir)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(dir).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
