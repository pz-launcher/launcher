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

    [Fact]
    public async Task ScanAsync_SteamMissing_ReturnsNotInstalledWarning()
    {
        var env = FakePlatformEnvironment.WithRegistrySteam(Path.Combine(_temp.Path, "NoSteamHere"));

        var result = await CreateService(env).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Installs);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(ErrorCode.SteamNotInstalled, warning.Code);
        Assert.Equal(ErrorKind.NotFound, warning.Kind);
    }

    [Fact]
    public async Task ScanAsync_NoPzAppsInstalled_ReturnsEmptyWithoutWarnings()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Installs);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ScanAsync_GameAndServerInDifferentLibraries_FindsBoth()
    {
        var root = _steam.CreateLibrary("Steam");
        var libA = _steam.CreateLibrary("LibA");
        var libB = _steam.CreateLibrary("LibB");
        _steam.WriteLibraryFolders(root, root, libA, libB);
        var game = _steam.AddApp(libA, PzSteamApps.Game, "ProjectZomboid", buildId: "25485521");
        var server = _steam.AddApp(libB, PzSteamApps.DedicatedServer, "Project Zomboid Dedicated Server", buildId: "25485538");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Warnings);
        Assert.Equal(
            [
                new PzInstall(PzInstallKind.Game, game, new SteamAppInfo("25485521", null)),
                new PzInstall(PzInstallKind.DedicatedServer, server, new SteamAppInfo("25485538", null)),
            ],
            result.Value.Installs);
    }

    [Fact]
    public async Task ScanAsync_MultipleLibrariesWithGame_ReturnsGamesFirstThenServersOrderedByPath()
    {
        var root = _steam.CreateLibrary("Steam");
        var libA = _steam.CreateLibrary("LibA");
        var libB = _steam.CreateLibrary("LibB");
        _steam.WriteLibraryFolders(root, libB, root, libA);
        var serverInRoot = _steam.AddApp(root, PzSteamApps.DedicatedServer, "Server");
        var gameInB = _steam.AddApp(libB, PzSteamApps.Game, "ProjectZomboid");
        var gameInA = _steam.AddApp(libA, PzSteamApps.Game, "ProjectZomboid");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.Equal([gameInA, gameInB, serverInRoot], result.Value!.Installs.Select(i => i.RootPath));
        Assert.Equal(
            [PzInstallKind.Game, PzInstallKind.Game, PzInstallKind.DedicatedServer],
            result.Value.Installs.Select(i => i.Kind));
    }

    [Fact]
    public async Task ScanAsync_BetaBranch_ReportsBranch()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid", betaKey: "unstable");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.Equal(new SteamAppInfo("1000", "unstable"), Assert.Single(result.Value!.Installs).Steam);
    }

    [Fact]
    public async Task ScanAsync_LibraryVdfMissing_ReturnsSteamLibraryUnreadableWarningAndScansRoot()
    {
        var root = _steam.CreateLibrary("Steam");
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(game, Assert.Single(result.Value.Installs).RootPath);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(ErrorCode.SteamLibraryUnreadable, warning.Code);
        Assert.Equal(ErrorKind.External, warning.Kind);
        Assert.Equal(SteamLibraryReader.LibraryFoldersPath(root), warning.Details["path"]);
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

        var result = await CreateService(env).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([game, serverInBroken], result.Value.Installs.Select(i => i.RootPath));
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(ErrorCode.SteamLibraryUnreadable, warning.Code);
        Assert.Equal(SteamLibraryReader.LibraryFoldersPath(brokenRoot), warning.Details["path"]);
    }

    [Fact]
    public async Task ScanAsync_LibraryVdfLocked_ReturnsSteamLibraryUnreadableWarning()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Exclusive file locks are only enforced on Windows.");
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");

        await using (new FileStream(SteamLibraryReader.LibraryFoldersPath(root), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

            Assert.Equal(game, Assert.Single(result.Value!.Installs).RootPath);
            Assert.Equal(ErrorCode.SteamLibraryUnreadable, Assert.Single(result.Value.Warnings).Code);
        }
    }

    [Fact]
    public async Task ScanAsync_ManifestMalformed_ReturnsManifestUnreadableWarningAndKeepsOthers()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");
        var manifest = SteamLibraryReader.ManifestPath(root, PzSteamApps.DedicatedServer);
        SteamLayoutBuilder.WriteRaw(manifest, "\"AppState\" { \"installdir\" ");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.Equal(game, Assert.Single(result.Value!.Installs).RootPath);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(ErrorCode.SteamManifestUnreadable, warning.Code);
        Assert.Equal(ErrorKind.External, warning.Kind);
        Assert.Equal(manifest, warning.Details["path"]);
    }

    [Fact]
    public async Task ScanAsync_ManifestFolderMissing_ReturnsFilesMissingWarning()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        var missing = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid", createFolder: false);

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Installs);
        var warning = Assert.Single(result.Value.Warnings);
        Assert.Equal(ErrorCode.InstallFilesMissing, warning.Code);
        Assert.Equal(ErrorKind.NotFound, warning.Kind);
        Assert.Equal(missing, warning.Details["path"]);
        Assert.Equal("108600", warning.Details["appId"]);
    }

    [Fact]
    public async Task ScanAsync_DuplicateLibraryPaths_ReportsEachInstallOnce()
    {
        var root = _steam.CreateLibrary("Steam");
        var lib = _steam.CreateLibrary("Lib");
        var libVariant = OperatingSystem.IsWindows()
            ? lib.ToUpperInvariant() + Path.DirectorySeparatorChar
            : lib + Path.DirectorySeparatorChar;
        _steam.WriteLibraryFolders(root, root, lib, libVariant, Path.Combine(lib, "..", "Lib"));
        _steam.AddApp(lib, PzSteamApps.Game, "ProjectZomboid");
        _steam.AddApp(root, PzSteamApps.DedicatedServer, "Server");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.Equal(2, result.Value!.Installs.Count);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ScanAsync_RegistryRootInOtherCasing_UsesLibraryFileSpelling()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Case-insensitive paths are a Windows behaviour.");
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        var game = _steam.AddApp(root, PzSteamApps.Game, "ProjectZomboid");
        var env = FakePlatformEnvironment.WithRegistrySteam(root.ToLowerInvariant().Replace('\\', '/'));

        var result = await CreateService(env).ScanAsync(Ct);

        Assert.Equal(game, Assert.Single(result.Value!.Installs).RootPath);
    }

    [Fact]
    public async Task ScanAsync_RelativeLibraryPath_IsIgnored()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root, "relative");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ScanAsync_LibraryPathWithNulChar_IsIgnoredAndOthersScanned()
    {
        var root = _steam.CreateLibrary("Steam");
        var lib = _steam.CreateLibrary("Lib");
        _steam.WriteLibraryFolders(root, root, lib + "\0broken", lib);
        var game = _steam.AddApp(lib, PzSteamApps.Game, "ProjectZomboid");

        var result = await CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(game, Assert.Single(result.Value.Installs).RootPath);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var root = _steam.CreateLibrary("Steam");
        _steam.WriteLibraryFolders(root, root);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(FakePlatformEnvironment.WithRegistrySteam(root)).ScanAsync(cts.Token));
    }
}
