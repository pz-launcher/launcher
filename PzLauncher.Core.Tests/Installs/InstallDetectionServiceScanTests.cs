using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Installs;

public sealed class InstallDetectionServiceScanTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SteamLayoutBuilder _steam;

    public InstallDetectionServiceScanTests() => _steam = new SteamLayoutBuilder(_temp.Path);

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static InstallDetectionService CreateService(FakePlatformEnvironment env) =>
        new(new SteamLocator(env), new SteamLibraryReader(), env, NullLogger<InstallDetectionService>.Instance);

    private static Task<InstallScanResult> ScanAsync(string registrySteamRoot) =>
        ScanAsync(FakePlatformEnvironment.WithRegistrySteam(registrySteamRoot));

    private static async Task<InstallScanResult> ScanAsync(FakePlatformEnvironment env) =>
        ResultAssert.Ok(await CreateService(env).ScanAsync(Ct));

    [Fact]
    public async Task ScanAsync_SteamMissing_ReturnsNotInstalledWarning()
    {
        var scan = await ScanAsync(Path.Combine(_temp.Path, "NoSteamHere"));

        Assert.Empty(scan.Installs);
        ResultAssert.SingleWarning(scan, ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task ScanAsync_NoPzAppsInstalled_ReturnsEmptyWithoutWarnings()
    {
        var scan = await ScanAsync(_steam.CreateSteamRoot());

        Assert.Empty(scan.Installs);
        Assert.Empty(scan.Warnings);
    }

    [Fact]
    public async Task ScanAsync_GameAndServerInDifferentLibraries_FindsBothWithBuildAndBranch()
    {
        var libA = _steam.CreateLibrary("LibA");
        var libB = _steam.CreateLibrary("LibB");
        var root = _steam.CreateSteamRoot(libA, libB);
        var game = _steam.AddApp(libA, PzSteamApps.Game, "ProjectZomboid", buildId: "25485521");
        var server = _steam.AddApp(libB, PzSteamApps.DedicatedServer, "Project Zomboid Dedicated Server", buildId: "25485538", betaKey: "unstable");

        var scan = await ScanAsync(root);

        Assert.Empty(scan.Warnings);
        Assert.Equal(
            [
                new PzInstall(PzInstallKind.Game, game, new SteamAppInfo("25485521", null)),
                new PzInstall(PzInstallKind.DedicatedServer, server, new SteamAppInfo("25485538", "unstable")),
            ],
            scan.Installs);
    }

    [Fact]
    public async Task ScanAsync_MultipleLibraries_ReturnsGamesFirstThenServersOrderedByPath()
    {
        var libB = _steam.CreateLibrary("LibB");
        var libA = _steam.CreateLibrary("LibA");
        var root = _steam.CreateSteamRoot(libB, libA);
        var serverInRoot = _steam.AddApp(root, PzSteamApps.DedicatedServer, "Server");
        var gameInB = _steam.AddApp(libB, PzSteamApps.Game, "ProjectZomboid");
        var gameInA = _steam.AddApp(libA, PzSteamApps.Game, "ProjectZomboid");

        var scan = await ScanAsync(root);

        Assert.Equal(
            [(PzInstallKind.Game, gameInA), (PzInstallKind.Game, gameInB), (PzInstallKind.DedicatedServer, serverInRoot)],
            scan.Installs.Select(i => (i.Kind, i.RootPath)));
    }

    [Fact]
    public async Task ScanAsync_LibraryVdfMissing_ReturnsSteamLibraryUnreadableWarningAndScansRoot()
    {
        var root = _steam.CreateLibrary("Steam");
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");

        var scan = await ScanAsync(root);

        Assert.Equal(game, Assert.Single(scan.Installs).RootPath);
        ResultAssert.SingleWarning(scan, ErrorCode.SteamLibraryUnreadable);
    }

    [Fact]
    public async Task ScanAsync_LibraryVdfMalformed_WarnsAndStillScansOtherSteamRoots()
    {
        var brokenRoot = _steam.CreateLibrary("BrokenSteam");
        SteamLayoutBuilder.WriteRaw(SteamLibraryReader.LibraryFoldersPath(brokenRoot), "\"libraryfolders\" { \"0\" { \"path\" ");
        var serverInBroken = _steam.AddApp(brokenRoot, PzSteamApps.DedicatedServer, "Server");

        var programFiles = _temp.CreateDirectory("PF86");
        var goodRoot = _steam.CreateLibrary(Path.Combine("PF86", "Steam"));
        var lib = _steam.CreateLibrary("Lib");
        _steam.WriteLibraryFolders(goodRoot, goodRoot, lib);
        var game = _steam.AddApp(lib, PzSteamApps.Game, "ProjectZomboid");

        var env = FakePlatformEnvironment.WithRegistrySteam(brokenRoot);
        env.EnvironmentVariables["ProgramFiles(x86)"] = programFiles;

        var scan = await ScanAsync(env);

        Assert.Equal([game, serverInBroken], scan.Installs.Select(i => i.RootPath));
        var warning = ResultAssert.SingleWarning(scan, ErrorCode.SteamLibraryUnreadable);
        Assert.Equal(SteamLibraryReader.LibraryFoldersPath(brokenRoot), warning.Details["path"]);
    }

    [Fact]
    public async Task ScanAsync_ManifestMalformed_ReturnsManifestUnreadableWarningAndKeepsOthers()
    {
        var root = _steam.CreateSteamRoot();
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");
        SteamLayoutBuilder.WriteRaw(SteamLibraryReader.ManifestPath(root, PzSteamApps.DedicatedServer), "\"AppState\" { \"installdir\" ");

        var scan = await ScanAsync(root);

        Assert.Equal(game, Assert.Single(scan.Installs).RootPath);
        ResultAssert.SingleWarning(scan, ErrorCode.SteamManifestUnreadable);
    }

    [Fact]
    public async Task ScanAsync_ManifestFolderMissing_ReturnsFilesMissingWarning()
    {
        var root = _steam.CreateSteamRoot();
        var missing = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid", createFolder: false);

        var scan = await ScanAsync(root);

        Assert.Empty(scan.Installs);
        var warning = ResultAssert.SingleWarning(scan, ErrorCode.InstallFilesMissing);
        Assert.Equal(missing, warning.Details["path"]);
        Assert.Equal("108600", warning.Details["appId"]);
    }

    [Fact]
    public async Task ScanAsync_DuplicateAndInvalidLibraryPaths_ReportsEachInstallOnceWithoutWarnings()
    {
        var lib = _steam.CreateLibrary("Lib");
        var libVariant = (OperatingSystem.IsWindows() ? lib.ToUpperInvariant() : lib) + Path.DirectorySeparatorChar;
        var root = _steam.CreateSteamRoot(lib, libVariant, Path.Combine(lib, "..", "Lib"), "relative", lib + "\0broken");
        var game = _steam.AddApp(lib, PzSteamApps.Game, "ProjectZomboid");
        var server = _steam.AddApp(root, PzSteamApps.DedicatedServer, "Server");

        var scan = await ScanAsync(root);

        Assert.Equal([game, server], scan.Installs.Select(i => i.RootPath));
        Assert.Empty(scan.Warnings);
    }

    [Fact]
    public async Task ScanAsync_RegistryRootInOtherCasing_UsesLibraryFileSpelling()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Case-insensitive paths are a Windows behaviour.");
        var root = _steam.CreateSteamRoot();
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");

        var scan = await ScanAsync(root.ToLowerInvariant().Replace('\\', '/'));

        Assert.Equal(game, Assert.Single(scan.Installs).RootPath);
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService(FakePlatformEnvironment.WithRegistrySteam(_steam.CreateSteamRoot()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(cts.Token));
    }
}
