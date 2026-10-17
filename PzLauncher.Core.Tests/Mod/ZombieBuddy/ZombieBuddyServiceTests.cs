using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.Installs;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Mod.ZombieBuddy.ZombieBuddyChannel;
using static PzLauncher.Core.Mod.ZombieBuddy.ZombieBuddyStep;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy;

public sealed class ZombieBuddyServiceTests : IDisposable
{
    private const string Native = "-agentlib:zbNative";
    private const string Java = "-javaagent:ZombieBuddy.jar";
    private const string PatchJar = "Skip.jar";
    private const string PatchPackage = "com.example.skip";
    private const string PatchEntry = PatchJar + ":" + PatchPackage;

    private readonly TempDirectory _temp = new();
    private readonly SteamLayoutBuilder _steam;
    private readonly string _steamRoot;
    private readonly string _library;
    private FakePlatformEnvironment _platform;
    private ZombieBuddyService _service;

    public ZombieBuddyServiceTests()
    {
        _steam = new SteamLayoutBuilder(_temp.Path);
        _library = _steam.CreateLibrary("Library");
        _steamRoot = _steam.CreateSteamRoot(_library);
        _platform = FakePlatformEnvironment.WithRegistrySteam(_steamRoot);
        _service = CreateService();
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ZombieBuddyService CreateService() => CreateService(_platform);

    internal static ZombieBuddyService CreateService(FakePlatformEnvironment platform) => new(
        InstallDetectionServiceScanTests.CreateService(platform),
        CreateWorkshopLibraries(platform),
        platform,
        NullLogger<ZombieBuddyService>.Instance);

    internal static WorkshopLibraries CreateWorkshopLibraries(FakePlatformEnvironment platform) =>
        new(new SteamLocator(platform), new SteamLibraryReader(), platform, NullLogger<WorkshopLibraries>.Instance);

    private void UseLinux()
    {
        _platform = new FakePlatformEnvironment { Os = PlatformKind.Linux, PathComparison = StringComparison.Ordinal, HomeDirectory = _temp.CreateDirectory("home") };
        _service = CreateService();
    }

    private GameInstallBuilder InstallsIn(string library) => new(Path.Combine(library, "steamapps", "common"), _platform.Os);

    private string CreateGame(string? json = null) =>
        InstallsIn(_library).CreateFlat("ProjectZomboid", json ?? GameInstallBuilder.WindowsGameJson, withJre: false);

    private static string JsonWith(params string[] args) => GameInstallBuilder.WindowsGameJsonWith(args);

    private string Subscribe(ZombieBuddyChannel channel, string? library = null)
    {
        var item = ZombieBuddyFiles.ItemFolder(library ?? _library, channel);
        var libs = Directory.CreateDirectory(ZombieBuddyFiles.LibsFolder(item)).FullName;
        foreach (var file in new[] { ZombieBuddyFiles.NativeLoader, ZombieBuddyFiles.AgentJar })
        {
            File.WriteAllText(Path.Combine(libs, file), $"{channel} {file}");
        }

        return item;
    }

    private static void WriteFiles(string game, params string[] files)
    {
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(game, file), "old");
        }
    }

    private static string ConfigOf(string game) => Path.Combine(game, "ProjectZomboid64.json");

    internal static List<string> VmArgsOf(string game) =>
        [.. JsonNode.Parse(File.ReadAllText(ConfigOf(game)))!["vmArgs"]!.AsArray().Select(arg => arg!.GetValue<string>())];

    private static List<string> ShippedVmArgs => [.. JsonNode.Parse(GameInstallBuilder.WindowsGameJson)!["vmArgs"]!.AsArray().Select(arg => arg!.GetValue<string>())];

    private async Task<ZombieBuddyStatus> StatusAsync(string root) => ResultAssert.Ok(await _service.GetStatusAsync(root, Ct));

    [Fact]
    public async Task GetStatusAsync_NotSubscribedNoLeftovers_NeedsNothing()
    {
        var status = await StatusAsync(CreateGame());

        Assert.True(status.IsSupported);
        Assert.Empty(status.Subscribed);
        Assert.False(status.HasLeftovers);
        Assert.Equal(ZombieBuddyAction.None, status.NeededAction);
    }

    [Fact]
    public async Task GetStatusAsync_Subscribed_NeedsInitialize()
    {
        var root = CreateGame();
        var item = Subscribe(Stable);

        var status = await StatusAsync(root);

        Assert.Equal([new ZombieBuddyItem(Stable, item)], status.Subscribed);
        Assert.False(status.IsInitialized);
        Assert.Equal(ZombieBuddyAction.Initialize, status.NeededAction);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ZombieBuddy.jar", true)]
    [InlineData("zbNative.dll,ZombieBuddy.jar", false)]
    public async Task GetStatusAsync_SubscribedWithFileOrArgMissing_NeedsInitialize(string files, bool withArg)
    {
        var root = CreateGame(withArg ? JsonWith(Native) : null);
        WriteFiles(root, files.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Subscribe(Stable);

        Assert.Equal(ZombieBuddyAction.Initialize, (await StatusAsync(root)).NeededAction);
    }

    [Theory]
    [InlineData("zbNative.dll", null)]
    [InlineData("ZombieBuddy.jar", null)]
    [InlineData("ZombieBuddy.jar.new", null)]
    [InlineData(null, Native + "=policy=allow-all")]
    [InlineData(null, Java)]
    public async Task GetStatusAsync_NotSubscribedWithALeftover_NeedsRemove(string? file, string? arg)
    {
        var root = CreateGame(arg is null ? null : JsonWith(arg));
        WriteFiles(root, file is null ? [] : [file]);

        var status = await StatusAsync(root);

        Assert.True(status.HasLeftovers);
        Assert.Equal(ZombieBuddyAction.Remove, status.NeededAction);
    }

    [Fact]
    public async Task GetStatusAsync_BothChannels_ListsStableFirst()
    {
        var root = CreateGame();
        var beta = Subscribe(Beta);
        var stable = Subscribe(Stable);

        Assert.Equal([new ZombieBuddyItem(Stable, stable), new ZombieBuddyItem(Beta, beta)], (await StatusAsync(root)).Subscribed);
    }

    [Fact]
    public async Task GetStatusAsync_ItemInOwnLibraryAndSteamRoot_UsesOwnLibrary()
    {
        var root = CreateGame();
        Subscribe(Stable, _steamRoot);
        var own = Subscribe(Stable);

        Assert.Equal([new ZombieBuddyItem(Stable, own)], (await StatusAsync(root)).Subscribed);
    }

    [Fact]
    public async Task GetStatusAsync_ItemInAnotherListedLibrary_FindsIt()
    {
        var other = _steam.CreateLibrary("Other");
        _steam.WriteLibraryFolders(_steamRoot, _steamRoot, _library, other);
        var root = CreateGame();
        var item = Subscribe(Beta, other);

        Assert.Equal([new ZombieBuddyItem(Beta, item)], (await StatusAsync(root)).Subscribed);
    }

    [Fact]
    public async Task GetStatusAsync_ManualInstallWithoutSteam_IsNotSubscribed()
    {
        _platform.SteamRegistryPath = Path.Combine(_temp.Path, "NoSteam");
        var root = new GameInstallBuilder(_temp.CreateDirectory("Games"), PlatformKind.Windows)
            .CreateFlat("PZ", GameInstallBuilder.WindowsGameJson, withJre: false);
        Subscribe(Stable);

        var status = await StatusAsync(root);

        Assert.Empty(status.Subscribed);
        Assert.Equal(ZombieBuddyAction.None, status.NeededAction);
    }

    [Fact]
    public async Task GetStatusAsync_LibraryListUnreadable_StillSearchesSteamRoot()
    {
        SteamLayoutBuilder.WriteRaw(SteamLibraryReader.LibraryFoldersPath(_steamRoot), "\"libraryfolders\" {");
        var root = new GameInstallBuilder(_temp.CreateDirectory("Games"), PlatformKind.Windows)
            .CreateFlat("PZ", GameInstallBuilder.WindowsGameJson, withJre: false);
        var item = Subscribe(Stable, _steamRoot);

        Assert.Equal([new ZombieBuddyItem(Stable, item)], (await StatusAsync(root)).Subscribed);
    }

    [Fact]
    public async Task GetStatusAsync_AgentArgAllowsAll_ReportsIt()
    {
        var root = CreateGame(JsonWith(Native + "=verbosity=1,policy=allow-all"));

        Assert.True((await StatusAsync(root)).IsAllowAll);
    }

    [Fact]
    public async Task GetStatusAsync_NoConfigDirOption_ConfigFolderIsInHome()
    {
        _platform.HomeDirectory = _temp.CreateDirectory("home");
        var root = CreateGame(JsonWith(Native + "=policy=allow-all"));

        Assert.Equal(Path.Combine(_platform.HomeDirectory, ".zombie_buddy"), (await StatusAsync(root)).ConfigFolder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStatusAsync_ConfigDirOption_IsTheConfigFolder(bool relative)
    {
        var configDir = relative ? "zb config" : Path.Combine(_temp.Path, "zb");
        var root = CreateGame(JsonWith($"{Native}=config_dir= {configDir} ,policy=allow-all"));

        Assert.Equal(relative ? Path.Combine(root, configDir) : configDir, (await StatusAsync(root)).ConfigFolder);
    }

    [Fact]
    public async Task GetStatusAsync_HomeUnresolvable_HasNoConfigFolder()
    {
        _platform.HomeDirectory = "";

        Assert.Null((await StatusAsync(CreateGame())).ConfigFolder);
    }

    [Fact]
    public async Task GetStatusAsync_Server_IsUnsupported()
    {
        var root = InstallsIn(_library).CreateServer("Server");
        Subscribe(Stable);

        Assert.Equal(ZombieBuddyStatus.Unsupported, await StatusAsync(root));
    }

    [Fact]
    public async Task GetStatusAsync_MacOS_IsUnsupported()
    {
        var root = CreateGame();
        Subscribe(Stable);
        _platform.Os = PlatformKind.MacOS;

        Assert.Equal(ZombieBuddyStatus.Unsupported, await StatusAsync(root));
    }

    [Fact]
    public async Task GetStatusAsync_ConfigMissing_ReturnsConfigMissing()
    {
        var root = InstallsIn(_library).CreateFlat("ProjectZomboid", json: null, withJre: false);

        ResultAssert.Failed(await _service.GetStatusAsync(root, Ct), ErrorCode.LaunchConfigMissing);
    }

    [Fact]
    public async Task GetStatusAsync_InvalidPath_PassesInstallErrorThrough() =>
        ResultAssert.Failed(await _service.GetStatusAsync("relative", Ct), ErrorCode.InstallInvalidPath);

    [Fact]
    public async Task GetStatusAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.GetStatusAsync(CreateGame(), cts.Token));
    }

    [Fact]
    public async Task InitializeAsync_Windows_CopiesBothFilesPrependsAgentArgAndReportsEachStep()
    {
        var root = CreateGame();
        Subscribe(Stable);
        var progress = new ListProgress<ZombieBuddyProgress>();

        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, progress, Ct));

        Assert.Equal("Stable zbNative.dll", await File.ReadAllTextAsync(Path.Combine(root, "zbNative.dll"), Ct));
        Assert.Equal("Stable ZombieBuddy.jar", await File.ReadAllTextAsync(Path.Combine(root, "ZombieBuddy.jar"), Ct));
        Assert.Equal([Native, .. ShippedVmArgs], VmArgsOf(root));
        Assert.All(progress.Reports, report => Assert.Equal([FindItem, CopyNativeLoader, CopyAgentJar, AddAgentArg], report.Steps));
        Assert.Equal([(0, false), (1, false), (2, false), (3, false), (4, true)], progress.Reports.Select(r => (r.Current, r.IsComplete)));
        var status = await StatusAsync(root);
        Assert.True(status.IsInitialized);
        Assert.Equal(ZombieBuddyAction.None, status.NeededAction);
    }

    [Fact]
    public async Task InitializeAsync_RunAgain_RefreshesFilesAndKeepsOneAgentArg()
    {
        var root = CreateGame();
        var item = Subscribe(Stable);
        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, ct: Ct));
        await File.WriteAllTextAsync(Path.Combine(ZombieBuddyFiles.LibsFolder(item), "ZombieBuddy.jar"), "updated", Ct);

        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, ct: Ct));

        Assert.Equal("updated", await File.ReadAllTextAsync(Path.Combine(root, "ZombieBuddy.jar"), Ct));
        Assert.Equal([Native, .. ShippedVmArgs], VmArgsOf(root));
    }

    [Fact]
    public async Task InitializeAsync_AgentArgWithOptions_LeavesTheJsonUntouched()
    {
        var json = JsonWith(Native + "=policy=allow-all");
        var root = CreateGame(json);
        Subscribe(Stable);

        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, ct: Ct));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task InitializeAsync_BothChannels_CopiesFromTheChosenOne()
    {
        var root = CreateGame();
        Subscribe(Stable);
        Subscribe(Beta);

        ResultAssert.Ok(await _service.InitializeAsync(root, Beta, ct: Ct));

        Assert.Equal("Beta ZombieBuddy.jar", await File.ReadAllTextAsync(Path.Combine(root, "ZombieBuddy.jar"), Ct));
    }

    [Fact]
    public async Task InitializeAsync_ChannelNotSubscribed_FailsAtFindItem()
    {
        var root = CreateGame();
        Subscribe(Stable);
        var progress = new ListProgress<ZombieBuddyProgress>();

        var error = ResultAssert.Failed(await _service.InitializeAsync(root, Beta, progress, Ct), ErrorCode.ZombieBuddyNotSubscribed);

        Assert.Equal("3812624292", error.Details["itemId"]);
        Assert.Equal(0, progress.Reports[^1].Current);
        Assert.Equal(GameInstallBuilder.WindowsGameJson, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task InitializeAsync_SourceFileMissing_FailsBeforeCopying()
    {
        var root = CreateGame();
        var source = Path.Combine(ZombieBuddyFiles.LibsFolder(Subscribe(Stable)), "ZombieBuddy.jar");
        File.Delete(source);

        var error = ResultAssert.Failed(await _service.InitializeAsync(root, Stable, ct: Ct), ErrorCode.ZombieBuddySourceFileMissing);

        Assert.Equal(source, error.Details["path"]);
        Assert.False(File.Exists(Path.Combine(root, "zbNative.dll")));
    }

    [Fact]
    public async Task InitializeAsync_TargetLocked_FailsAtThatStepAndRetrySucceeds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to overwrite an open file.");
        var root = CreateGame();
        Subscribe(Stable);
        var jar = Path.Combine(root, "ZombieBuddy.jar");
        WriteFiles(root, "ZombieBuddy.jar");
        var progress = new ListProgress<ZombieBuddyProgress>();

        using (File.Open(jar, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = ResultAssert.Failed(await _service.InitializeAsync(root, Stable, progress, Ct), ErrorCode.ZombieBuddyCopyFailed);
            Assert.Equal(jar, error.Details["path"]);
        }

        Assert.Equal(CopyAgentJar, progress.Reports[^1].Steps[progress.Reports[^1].Current]);
        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, ct: Ct));
        Assert.Equal("Stable ZombieBuddy.jar", await File.ReadAllTextAsync(jar, Ct));
    }

    [Fact]
    public async Task InitializeAsync_CancelledDuringAStep_StopsBeforeTheNext()
    {
        var root = CreateGame();
        Subscribe(Stable);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new ListProgress<ZombieBuddyProgress>(report =>
        {
            if (report.Current == 1)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.InitializeAsync(root, Stable, progress, cts.Token));

        Assert.True(File.Exists(Path.Combine(root, "zbNative.dll")));
        Assert.False(File.Exists(Path.Combine(root, "ZombieBuddy.jar")));
        Assert.Equal(ShippedVmArgs, VmArgsOf(root));
    }

    [Fact]
    public async Task InitializeAsync_LinuxNestedLayout_CopiesOnlyTheJarAndAddsJavaAgent()
    {
        UseLinux();
        var root = InstallsIn(_library).CreateNested("ProjectZomboid", GameInstallBuilder.LinuxGameJson, withJre: false);
        var game = GameInstallBuilder.GameFolder(root, nested: true);
        Subscribe(Stable);
        var progress = new ListProgress<ZombieBuddyProgress>();

        ResultAssert.Ok(await _service.InitializeAsync(root, Stable, progress, Ct));

        Assert.Equal("Stable ZombieBuddy.jar", await File.ReadAllTextAsync(Path.Combine(game, "ZombieBuddy.jar"), Ct));
        Assert.False(File.Exists(Path.Combine(game, "zbNative.dll")));
        Assert.Equal(Java, VmArgsOf(game)[0]);
        Assert.Equal([FindItem, CopyAgentJar, AddAgentArg], progress.Reports[^1].Steps);
        Assert.True((await StatusAsync(root)).IsInitialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operations_ServerOrMacOS_ReturnTheReason(bool macOS)
    {
        var root = macOS ? CreateGame(JsonWith(Native)) : InstallsIn(_library).CreateServer("Server");
        Subscribe(Stable);
        if (macOS)
        {
            _platform.Os = PlatformKind.MacOS;
        }

        var code = macOS ? ErrorCode.LaunchPlatformUnsupported : ErrorCode.LaunchNotAGame;
        ResultAssert.Failed(await _service.InitializeAsync(root, Stable, ct: Ct), code);
        ResultAssert.Failed(await _service.RemoveAsync(root, ct: Ct), code);
        ResultAssert.Failed(await _service.SetAllowAllAsync(root, true, Ct), code);
        ResultAssert.Failed(await _service.EnsurePatchesJarAsync(root, SourceJar("v1"), PatchJar, PatchPackage, Ct), code);
        ResultAssert.Failed(await _service.RemovePatchesJarAsync(root, PatchJar, PatchPackage, Ct), code);
    }

    [Fact]
    public async Task RemoveAsync_Leftovers_DeletesFilesAndEveryAgentArg()
    {
        var root = CreateGame(JsonWith(Native + "=verbosity=1", Java));
        WriteFiles(root, "zbNative.dll", "ZombieBuddy.jar", "ZombieBuddy.jar.new");
        var progress = new ListProgress<ZombieBuddyProgress>();

        ResultAssert.Ok(await _service.RemoveAsync(root, progress, Ct));

        Assert.Empty(Directory.EnumerateFiles(root, "*ZombieBuddy*").Concat(Directory.EnumerateFiles(root, "zbNative*")));
        Assert.Equal(ShippedVmArgs, VmArgsOf(root));
        Assert.Equal([DeleteFiles, RemoveAgentArg], progress.Reports[^1].Steps);
        Assert.Equal((2, true), (progress.Reports[^1].Current, progress.Reports[^1].IsComplete));
        var status = await StatusAsync(root);
        Assert.False(status.HasLeftovers);
        Assert.Equal(ZombieBuddyAction.None, status.NeededAction);
    }

    [Fact]
    public async Task RemoveAsync_NothingLeft_SucceedsWithoutWriting()
    {
        var root = CreateGame();

        ResultAssert.Ok(await _service.RemoveAsync(root, ct: Ct));

        Assert.Equal(GameInstallBuilder.WindowsGameJson, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task RemoveAsync_FileLocked_ReturnsDeleteFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete an open file.");
        var root = CreateGame();
        WriteFiles(root, "ZombieBuddy.jar");
        var jar = Path.Combine(root, "ZombieBuddy.jar");

        using var locked = File.Open(jar, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = ResultAssert.Failed(await _service.RemoveAsync(root, ct: Ct), ErrorCode.ZombieBuddyDeleteFailed);

        Assert.Equal(jar, error.Details["path"]);
    }

    [Fact]
    public async Task SetAllowAllAsync_OnThenOff_RewritesThePolicyAndStatusFollows()
    {
        var root = CreateGame(JsonWith(Native + "=verbosity=1"));

        ResultAssert.Ok(await _service.SetAllowAllAsync(root, true, Ct));
        Assert.Equal(Native + "=verbosity=1,policy=allow-all", VmArgsOf(root)[0]);
        Assert.True((await StatusAsync(root)).IsAllowAll);

        ResultAssert.Ok(await _service.SetAllowAllAsync(root, false, Ct));
        Assert.Equal(Native + "=verbosity=1", VmArgsOf(root)[0]);
        Assert.False((await StatusAsync(root)).IsAllowAll);
    }

    [Fact]
    public async Task SetAllowAllAsync_AlreadyThatPolicy_DoesNotWrite()
    {
        var json = JsonWith(Native + "=policy=allow-all");
        var root = CreateGame(json);

        ResultAssert.Ok(await _service.SetAllowAllAsync(root, true, Ct));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task SetAllowAllAsync_NoAgentArg_ReturnsNotInitialized()
    {
        var root = CreateGame();

        ResultAssert.Failed(await _service.SetAllowAllAsync(root, true, Ct), ErrorCode.ZombieBuddyNotInitialized);
    }

    private string SourceJar(string content)
    {
        var path = Path.Combine(_temp.CreateDirectory("mod"), PatchJar);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<Result> EnsurePatchesJarAsync(string root, string source) => _service.EnsurePatchesJarAsync(root, source, PatchJar, PatchPackage, Ct);

    private Task<Result> RemovePatchesJarAsync(string root) => _service.RemovePatchesJarAsync(root, PatchJar, PatchPackage, Ct);

    [Theory]
    [InlineData(Native + "=policy=allow-all", Native + "=policy=allow-all,patches_jar=" + PatchEntry)]
    [InlineData(Native + "=patches_jar=other.jar:x.y,policy=allow-all", Native + "=patches_jar=other.jar:x.y;" + PatchEntry + ",policy=allow-all")]
    [InlineData(Native + @"=patches_jar=E:\mods\Skip.jar:" + PatchPackage, Native + "=patches_jar=" + PatchEntry)]
    public async Task EnsurePatchesJarAsync_NoCopy_CopiesTheJarThenAddsTheEntry(string agentArg, string expected)
    {
        var root = CreateGame(JsonWith(agentArg));

        ResultAssert.Ok(await EnsurePatchesJarAsync(root, SourceJar("v1")));

        Assert.Equal("v1", await File.ReadAllTextAsync(Path.Combine(root, PatchJar), Ct));
        Assert.Equal([expected, .. ShippedVmArgs], VmArgsOf(root));
    }

    [Fact]
    public async Task EnsurePatchesJarAsync_StaleCopy_ReplacesIt()
    {
        var root = CreateGame(JsonWith(Native + "=patches_jar=" + PatchEntry));
        await File.WriteAllTextAsync(Path.Combine(root, PatchJar), "old", Ct);

        ResultAssert.Ok(await EnsurePatchesJarAsync(root, SourceJar("v2")));

        Assert.Equal("v2", await File.ReadAllTextAsync(Path.Combine(root, PatchJar), Ct));
    }

    [Fact]
    public async Task EnsurePatchesJarAsync_AlreadySetUp_WritesNeitherFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to overwrite an open file.");
        var json = JsonWith(Native + "=policy=allow-all,patches_jar=" + PatchEntry);
        var root = CreateGame(json);
        var source = SourceJar("v1");
        File.Copy(source, Path.Combine(root, PatchJar));

        // Still readable for the compare, but a copy over it would fail.
        using (File.Open(Path.Combine(root, PatchJar), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ResultAssert.Ok(await EnsurePatchesJarAsync(root, source));
        }

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task EnsurePatchesJarAsync_NoAgentArg_DoesNothing()
    {
        var root = CreateGame();

        ResultAssert.Ok(await EnsurePatchesJarAsync(root, SourceJar("v1")));

        Assert.False(File.Exists(Path.Combine(root, PatchJar)));
        Assert.Equal(GameInstallBuilder.WindowsGameJson, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task EnsurePatchesJarAsync_CopyLocked_ReturnsCopyFailedAndLeavesTheJson()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to overwrite an open file.");
        var json = JsonWith(Native + "=policy=allow-all");
        var root = CreateGame(json);
        var jar = Path.Combine(root, PatchJar);
        await File.WriteAllTextAsync(jar, "old", Ct);

        using var locked = File.Open(jar, FileMode.Open, FileAccess.Read, FileShare.None);
        var error = ResultAssert.Failed(await EnsurePatchesJarAsync(root, SourceJar("v1")), ErrorCode.ZombieBuddyCopyFailed);

        Assert.Equal(jar, error.Details["path"]);
        Assert.Equal(json, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Theory]
    [InlineData(Native + "=policy=allow-all,patches_jar=" + PatchEntry, Native + "=policy=allow-all")]
    [InlineData(Native + "=patches_jar=other.jar:x.y;" + PatchEntry + ",policy=prompt", Native + "=patches_jar=other.jar:x.y,policy=prompt")]
    public async Task RemovePatchesJarAsync_SetUp_RemovesTheEntryAndTheJar(string agentArg, string expected)
    {
        var root = CreateGame(JsonWith(agentArg));
        await File.WriteAllTextAsync(Path.Combine(root, PatchJar), "v1", Ct);

        ResultAssert.Ok(await RemovePatchesJarAsync(root));

        Assert.False(File.Exists(Path.Combine(root, PatchJar)));
        Assert.Equal([expected, .. ShippedVmArgs], VmArgsOf(root));
    }

    [Fact]
    public async Task RemovePatchesJarAsync_NothingThere_WritesNothing()
    {
        var json = JsonWith(Native + "=policy=allow-all,patches_jar=other.jar:x.y");
        var root = CreateGame(json);

        ResultAssert.Ok(await RemovePatchesJarAsync(root));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigOf(root), Ct));
    }

    [Fact]
    public async Task RemovePatchesJarAsync_JarLocked_ReturnsDeleteFailedAfterRemovingTheEntry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete an open file.");
        var root = CreateGame(JsonWith(Native + "=policy=allow-all,patches_jar=" + PatchEntry));
        var jar = Path.Combine(root, PatchJar);
        await File.WriteAllTextAsync(jar, "v1", Ct);

        using var locked = File.Open(jar, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = ResultAssert.Failed(await RemovePatchesJarAsync(root), ErrorCode.ZombieBuddyDeleteFailed);

        Assert.Equal(jar, error.Details["path"]);
        Assert.Equal(Native + "=policy=allow-all", VmArgsOf(root)[0]);
    }
}
