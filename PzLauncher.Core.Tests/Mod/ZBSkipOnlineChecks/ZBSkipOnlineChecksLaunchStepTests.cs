using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZBSkipOnlineChecks;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.ZBSkipOnlineChecks;

public sealed class ZBSkipOnlineChecksLaunchStepTests : IDisposable
{
    private const string AllowAll = "-agentlib:zbNative=policy=allow-all";
    private const string Prompt = "-agentlib:zbNative=policy=prompt";
    internal const string Entry = "ZBSkipOnlineChecks.jar:com.dont39.pz.zbskiponlinechecks";

    // The builder's jar path; the game-folder copy is named after the mod either way.
    private const string SkipModInfo =
        "name=ZB Skip Online Checks\nid=ZBSkipOnlineChecks\njavaJarFile=media/java/Zabric.jar\njavaPkgName=com.dont39.pz.zbskiponlinechecks\njavaPreload=true\n";

    private readonly TempDirectory _temp = new();
    private readonly string _library;
    private readonly string _home;
    private readonly FakePlatformEnvironment _platform;
    private readonly ZombieBuddyService _zombieBuddy;
    private readonly ZBSkipOnlineChecksLaunchStep _step;

    public ZBSkipOnlineChecksLaunchStepTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(_library));
        _platform.HomeDirectory = _home;
        _zombieBuddy = ZombieBuddyServiceTests.CreateService(_platform);
        _step = new ZBSkipOnlineChecksLaunchStep(_zombieBuddy, _platform, NullLogger<ZBSkipOnlineChecksLaunchStep>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DefaultConfig => Path.Combine(_home, ".zombie_buddy", "config.json");

    private string DefaultCache => Path.Combine(_home, "Zomboid");

    private GameInstallBuilder Installs => new(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows);

    private string CreateGame(string agentArg = AllowAll) =>
        Installs.CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJsonWith(agentArg), withJre: false);

    private string SteamCopy(string? manifest = ZabricBuilder.PreloadManifest) => SteamCopy(_library, manifest);

    internal static string SteamCopy(string library, string? manifest = ZabricBuilder.PreloadManifest) =>
        ZabricBuilder.Create(
            Path.Combine(WorkshopLibraries.ItemFolder(library, "3821335660"), "mods", "ZBSkipOnlineChecks"), modInfo: SkipModInfo, manifest: manifest);

    internal static string GameJar(string root) => Path.Combine(root, "ZBSkipOnlineChecks.jar");

    private static string GameJson(string root) => Path.Combine(root, "ProjectZomboid64.json");

    internal static void WriteEnabled(string cache, params string[] ids)
    {
        var mods = Directory.CreateDirectory(Path.Combine(cache, "mods")).FullName;
        File.WriteAllText(Path.Combine(mods, "default.txt"), "VERSION = 1,\nmods\n{\n" + string.Concat(ids.Select(id => $"    mod = {id},\n")) + "}\n");
    }

    private void WriteConfig(params string[] ids)
    {
        var preload = new JsonObject(ids.Select(id => KeyValuePair.Create(id, (JsonNode?)new JsonObject { ["infPath"] = $"{id}.info", ["jarPath"] = $"{id}.jar" })));
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultConfig)!);
        File.WriteAllText(DefaultConfig, new JsonObject { ["trusted_authors"] = new JsonArray(JsonValue.Create(1)), ["preload_mods"] = preload }.ToJsonString());
    }

    private JsonObject Preload() => JsonNode.Parse(File.ReadAllText(DefaultConfig))!["preload_mods"]!.AsObject();

    private async Task<Result> RunAsync(string root, string extraGameArgs = "", bool afterZabric = true)
    {
        var launch = await ZombieBuddyLaunchTests.LaunchAsync(_zombieBuddy, _platform, root, extraGameArgs);
        if (afterZabric)
        {
            launch.MarkPreloaded("Zabric");
        }

        return await _step.RunAsync(launch, Ct);
    }

    [Fact]
    public async Task RunAsync_EnabledAllowAllZabricPreloaded_SetsUpBothThenWritesNothing()
    {
        var root = CreateGame();
        var version = SteamCopy();
        WriteEnabled(DefaultCache, "ZombieBuddy", "Zabric", "ZBSkipOnlineChecks");
        WriteConfig("Zabric");

        ResultAssert.Ok(await RunAsync(root));

        var preload = Preload();
        Assert.Equal(["Zabric", "ZBSkipOnlineChecks"], preload.Select(mod => mod.Key));
        Assert.Equal(ZabricBuilder.InfPath(version), preload["ZBSkipOnlineChecks"]!["infPath"]!.GetValue<string>());
        Assert.Equal(ZabricBuilder.JarPath(version), preload["ZBSkipOnlineChecks"]!["jarPath"]!.GetValue<string>());
        Assert.Equal(await File.ReadAllBytesAsync(ZabricBuilder.JarPath(version), Ct), await File.ReadAllBytesAsync(GameJar(root), Ct));
        Assert.Equal(AllowAll + ",patches_jar=" + Entry, ZombieBuddyServiceTests.VmArgsOf(root)[0]);

        var config = await File.ReadAllTextAsync(DefaultConfig, Ct);
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);
        ResultAssert.Ok(await RunAsync(root));

        Assert.Equal(config, await File.ReadAllTextAsync(DefaultConfig, Ct));
        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
    }

    [Fact]
    public async Task RunAsync_ZabricNotPreloaded_SetsUpOnlyTheAgentStart()
    {
        var root = CreateGame();
        SteamCopy();
        WriteEnabled(DefaultCache, "ZBSkipOnlineChecks");

        ResultAssert.Ok(await RunAsync(root, afterZabric: false));

        Assert.False(File.Exists(DefaultConfig));
        Assert.True(File.Exists(GameJar(root)));
        Assert.Equal(AllowAll + ",patches_jar=" + Entry, ZombieBuddyServiceTests.VmArgsOf(root)[0]);
    }

    [Theory]
    [InlineData(Prompt, true)]
    [InlineData("-agentlib:zbNative", true)]
    [InlineData(AllowAll, false)]
    public async Task RunAsync_EnabledButNotAllowAllOrNotInstalled_ChangesNothing(string agentArg, bool installed)
    {
        var root = CreateGame(agentArg);
        if (installed)
        {
            SteamCopy();
        }

        WriteEnabled(DefaultCache, "ZBSkipOnlineChecks");
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);

        ResultAssert.Ok(await RunAsync(root));

        Assert.False(File.Exists(DefaultConfig));
        Assert.False(File.Exists(GameJar(root)));
        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
    }

    [Theory]
    [InlineData(AllowAll)]
    [InlineData(Prompt)]
    public async Task RunAsync_NotEnabled_RemovesBothAndKeepsTheRest(string agentArg)
    {
        var root = CreateGame($"{agentArg},patches_jar=other.jar:x.y;{Entry},verbosity=1");
        SteamCopy();
        await File.WriteAllTextAsync(GameJar(root), "manual copy", Ct);
        WriteEnabled(DefaultCache, "ZombieBuddy", "Zabric");
        WriteConfig("Zabric", "ZBSkipOnlineChecks", "OtherMod");

        ResultAssert.Ok(await RunAsync(root));

        Assert.Equal(["Zabric", "OtherMod"], Preload().Select(mod => mod.Key));
        Assert.Equal("[1]", JsonNode.Parse(await File.ReadAllTextAsync(DefaultConfig, Ct))!["trusted_authors"]!.ToJsonString());
        Assert.Equal($"{agentArg},patches_jar=other.jar:x.y,verbosity=1", ZombieBuddyServiceTests.VmArgsOf(root)[0]);
        Assert.False(File.Exists(GameJar(root)));
    }

    [Fact]
    public async Task RunAsync_NotEnabledAndNothingSetUp_WritesNothing()
    {
        var root = CreateGame();
        WriteConfig("Zabric");
        var config = await File.ReadAllTextAsync(DefaultConfig, Ct);
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);

        ResultAssert.Ok(await RunAsync(root));

        Assert.Equal(config, await File.ReadAllTextAsync(DefaultConfig, Ct));
        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
    }

    [Fact]
    public async Task RunAsync_CacheDirArg_ReadsItsEnabledListAndFindsItsCopyFirst()
    {
        var root = CreateGame();
        SteamCopy();
        var cache = Path.Combine(_temp.Path, "pz cache");
        var staged = ZabricBuilder.Create(
            Path.Combine(cache, "Workshop", "ZBSkipOnlineChecks", "Contents", "mods", "ZBSkipOnlineChecks"), modInfo: SkipModInfo, classes: "v2");
        WriteEnabled(cache, "ZBSkipOnlineChecks");
        WriteConfig("Zabric");

        ResultAssert.Ok(await RunAsync(root, $"-cachedir=\"{cache}\" -debug"));

        Assert.Equal(ZabricBuilder.JarPath(staged), Preload()["ZBSkipOnlineChecks"]!["jarPath"]!.GetValue<string>());
        Assert.Equal(await File.ReadAllBytesAsync(ZabricBuilder.JarPath(staged), Ct), await File.ReadAllBytesAsync(GameJar(root), Ct));
    }

    [Fact]
    public async Task RunAsync_EnabledListMalformed_ChangesNothing()
    {
        var root = CreateGame($"{AllowAll},patches_jar={Entry}");
        await File.WriteAllTextAsync(GameJar(root), "manual copy", Ct);
        Directory.CreateDirectory(Path.Combine(DefaultCache, "mods"));
        await File.WriteAllTextAsync(Path.Combine(DefaultCache, "mods", "default.txt"), "VERSION = 1,\nmods\n{\n    mod = Zabric,\n", Ct);
        WriteConfig("Zabric", "ZBSkipOnlineChecks");
        var config = await File.ReadAllTextAsync(DefaultConfig, Ct);
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);

        ResultAssert.Ok(await RunAsync(root));

        Assert.Equal(config, await File.ReadAllTextAsync(DefaultConfig, Ct));
        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
        Assert.True(File.Exists(GameJar(root)));
    }

    [Fact]
    public async Task RunAsync_CacheUnresolved_ChangesNothing()
    {
        var root = CreateGame($"{AllowAll},patches_jar={Entry}");
        await File.WriteAllTextAsync(GameJar(root), "manual copy", Ct);
        _platform.HomeDirectory = "";
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);

        ResultAssert.Ok(await RunAsync(root));

        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
        Assert.True(File.Exists(GameJar(root)));
    }

    [Fact]
    public async Task RunAsync_FoundCopyNotPreloadable_ReturnsTheReasonWithoutWriting()
    {
        var root = CreateGame();
        SteamCopy(manifest: "Manifest-Version: 1.0\r\n\r\n");
        WriteEnabled(DefaultCache, "ZBSkipOnlineChecks");
        var json = await File.ReadAllTextAsync(GameJson(root), Ct);

        var error = ResultAssert.Failed(await RunAsync(root), ErrorCode.ZombieBuddyModNotPreloadable);

        Assert.Equal(ZombieBuddyJavaMod.NoManifestPreload, error.Details["reason"]);
        Assert.False(File.Exists(DefaultConfig));
        Assert.False(File.Exists(GameJar(root)));
        Assert.Equal(json, await File.ReadAllTextAsync(GameJson(root), Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_ConfigInvalid_ReturnsItsErrorAndStillSyncsTheAgentStart(bool enabled)
    {
        var root = CreateGame(enabled ? AllowAll : $"{AllowAll},patches_jar={Entry}");
        SteamCopy();
        WriteEnabled(DefaultCache, enabled ? ["ZBSkipOnlineChecks"] : []);
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultConfig)!);
        await File.WriteAllTextAsync(DefaultConfig, "[]", Ct);

        ResultAssert.Failed(await RunAsync(root), ErrorCode.ZombieBuddyPreloadConfigInvalid);

        Assert.Equal("[]", await File.ReadAllTextAsync(DefaultConfig, Ct));
        Assert.Equal(enabled ? $"{AllowAll},patches_jar={Entry}" : AllowAll, ZombieBuddyServiceTests.VmArgsOf(root)[0]);
        Assert.Equal(enabled, File.Exists(GameJar(root)));
    }

    [Fact]
    public async Task RunAsync_CancelledToken_Throws()
    {
        var launch = await ZombieBuddyLaunchTests.LaunchAsync(_zombieBuddy, _platform, CreateGame());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _step.RunAsync(launch, cts.Token));
    }
}
