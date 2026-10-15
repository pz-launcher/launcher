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

    private string CreateFolder(params string[] entries)
    {
        var dir = _temp.CreateDirectory("folder");
        foreach (var entry in entries)
        {
            // macOS app bundles are directories.
            if (entry.EndsWith(".app", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(dir, entry));
            }
            else
            {
                File.WriteAllText(Path.Combine(dir, entry), "");
            }
        }

        return dir;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/folder")]
    [InlineData("bad\0path")]
    public async Task InspectAsync_InvalidPath_ReturnsInvalidPath(string path) =>
        ResultAssert.Failed(await _service.InspectAsync(path, Ct), ErrorCode.InstallInvalidPath);

    [Fact]
    public async Task InspectAsync_DirectoryMissing_ReturnsPathNotFound()
    {
        var missing = Path.Combine(_temp.Path, "nope");

        var error = ResultAssert.Failed(await _service.InspectAsync(missing, Ct), ErrorCode.InstallPathNotFound);

        Assert.Equal(missing, error.Details["path"]);
    }

    [Fact]
    public async Task InspectAsync_DirectoryNotListable_ReturnsPathUnreadable()
    {
        var dir = CreateFolder("StartServer64.bat");

        var result = OperatingSystem.IsWindows()
            ? await WithDeniedListingAsync(dir, () => _service.InspectAsync(dir, Ct))
            : await WithNoUnixPermissionsAsync(dir, () => _service.InspectAsync(dir, Ct));

        ResultAssert.Failed(result, ErrorCode.InstallPathUnreadable);
    }

    [Fact]
    public async Task InspectAsync_EmptyDirectory_ReturnsNotAPzInstall() =>
        ResultAssert.Failed(await _service.InspectAsync(CreateFolder(), Ct), ErrorCode.InstallNotAPzInstall);

    [Theory]
    [InlineData(PzInstallKind.DedicatedServer, "StartServer64.bat")]
    [InlineData(PzInstallKind.DedicatedServer, "start-server.sh")]
    [InlineData(PzInstallKind.DedicatedServer, "ProjectZomboid64.exe", "StartServer64.bat")]
    [InlineData(PzInstallKind.Game, "ProjectZomboid64.exe")]
    [InlineData(PzInstallKind.Game, "ProjectZomboid64")]
    [InlineData(PzInstallKind.Game, "projectzomboid.sh")]
    [InlineData(PzInstallKind.Game, "Project Zomboid.app")]
    public async Task InspectAsync_Markers_ReturnsManualInstallOfThatKindPreferringServer(PzInstallKind kind, params string[] markers)
    {
        var dir = CreateFolder(markers);

        var install = ResultAssert.Ok(await _service.InspectAsync(dir + Path.DirectorySeparatorChar, Ct));

        Assert.Equal(new PzInstall(kind, dir, Steam: null), install);
    }

    [Fact]
    public async Task InspectAsync_GameShipsServerBat_StillGame()
    {
        // The real game folder contains ProjectZomboidServer.bat (see research/install-markers.md).
        var dir = CreateFolder("ProjectZomboid64.exe", "ProjectZomboidServer.bat");

        Assert.Equal(PzInstallKind.Game, ResultAssert.Ok(await _service.InspectAsync(dir, Ct)).Kind);
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

    [UnsupportedOSPlatform("windows")]
    private static async Task<T> WithNoUnixPermissionsAsync<T>(string dir, Func<Task<T>> action)
    {
        File.SetUnixFileMode(dir, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanList(dir), "Running as a user that ignores permissions (root).");
            return await action();
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
