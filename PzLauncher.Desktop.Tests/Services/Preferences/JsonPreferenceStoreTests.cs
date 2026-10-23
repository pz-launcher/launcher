using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences.Secrets;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell.Lifetime;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Tests.Services.Preferences;

public sealed class JsonPreferenceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static readonly WindowPlacement Placement = new(-1910, 30, 1000.5, 640, IsMaximized: true);

    private static readonly string JavaA = OperatingSystem.IsWindows() ? @"C:\Java\jdk-21" : "/opt/java/jdk-21";

    private static readonly string JavaB = OperatingSystem.IsWindows() ? @"D:\Tools\jre 17" : "/home/player/jre 17";

    private static readonly string GameRoot =
        OperatingSystem.IsWindows() ? @"E:\SteamLibrary\steamapps\common\ProjectZomboid" : "/home/player/.local/share/Steam/steamapps/common/ProjectZomboid";

    private static readonly string OtherRoot = OperatingSystem.IsWindows() ? @"D:\Games\Project Zomboid" : "/opt/games/Project Zomboid";

    private static readonly SavedProxy ManualProxy = new(ProxyMode.Manual, "127.0.0.1", 7890, "alice", "s3cret: pässword");

    private static readonly GameLaunchOverrides Overrides = new(JavaA, 8192, 2048, "-Dzomboid.steam=1", "-debug", GarbageCollector.Shenandoah);

    private static readonly SavedWorkshopEntry SteamCollection = new(18446744073709551615, "B42 \"必备\" pack");

    private static readonly SavedWorkshopUserCollections UserCollections = new(
        new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero),
        new WorkshopUserCollections(
            12345678,
            [new WorkshopCollectionSummary(18446744073709551615, "合集 \"one\"", 201), new WorkshopCollectionSummary(2690796635, null, 0)],
            [new WorkshopCollectionSummary(3815132834, "my", 214)]));

    private static readonly SavedWorkshopFavorites Favorites = new(
        [SteamCollection],
        [new SavedWorkshopEntry(2002, "物品 \"one\""), new SavedWorkshopEntry(18446744073709551614, null)]);

    private static UserPreferences WithJava(params string[] homes) => new(null, null, null) { ManualJavaHomes = homes };

    private static UserPreferences WithChannels(params (string Root, ZombieBuddyChannel Channel)[] channels) =>
        new(null, null, null) { ZombieBuddyChannels = channels.ToDictionary(entry => entry.Root, entry => entry.Channel) };

    private static UserPreferences WithDefaultCollections(params (string Root, string Name)[] defaults) =>
        new(null, null, null) { DefaultModCollections = defaults.ToDictionary(entry => entry.Root, entry => entry.Name) };

    private static UserPreferences WithInstalls(params (string Root, GameLaunchOverrides Overrides)[] installs) =>
        new(null, null, null) { InstallOverrides = installs.ToDictionary(install => install.Root, install => install.Overrides) };

    public static TheoryData<AppTheme> Themes => [.. Enum.GetValues<AppTheme>()];

    public static TheoryData<WindowPlacement> Placements =>
    [
        Placement,
        new WindowPlacement(120, 80, 820, 520, IsMaximized: false),
    ];

    // In a folder that doesn't exist yet, as on first launch.
    private string FilePath => Path.Combine(_temp.Path, "PzLauncher", "preferences.json");

    private JsonPreferenceStore CreateStore(ISecretProtector? secrets = null) => new(FilePath, NullLogger.Instance, secrets ?? new PlainSecretProtector());

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public async Task SaveLanguageAsync_ThenLoad_RoundTrips(AppLanguage language)
    {
        await CreateStore().SaveLanguageAsync(language, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(language, null, null), CreateStore().Load());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SaveThemeAsync_ThenLoad_RoundTrips(AppTheme theme)
    {
        await CreateStore().SaveThemeAsync(theme, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, theme, null), CreateStore().Load());
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public async Task SaveWindowPlacementAsync_ThenLoad_RoundTrips(WindowPlacement placement)
    {
        await CreateStore().SaveWindowPlacementAsync(placement, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, placement), CreateStore().Load());
    }

    [Fact]
    public async Task SaveManualJavaHomesAsync_ThenLoad_RoundTripsInOrder()
    {
        await CreateStore().SaveManualJavaHomesAsync([JavaB, JavaA], TestContext.Current.CancellationToken);

        Assert.Equal(WithJava(JavaB, JavaA), CreateStore().Load());
    }

    [Fact]
    public async Task SaveManualJavaHomesAsync_Empty_WritesEmptyArrayAndLoadsNone()
    {
        var store = CreateStore();
        await store.SaveManualJavaHomesAsync([JavaA], TestContext.Current.CancellationToken);

        await store.SaveManualJavaHomesAsync([], TestContext.Current.CancellationToken);

        Assert.Equal("""{"javaRuntimes":[]}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
        Assert.Equal(WithJava(), CreateStore().Load());
    }

    [Fact]
    public async Task SaveInstallOverridesAsync_ThenLoad_RoundTrips()
    {
        var store = CreateStore();

        await store.SaveInstallOverridesAsync(GameRoot, Overrides, TestContext.Current.CancellationToken);
        await store.SaveInstallOverridesAsync(OtherRoot, GameLaunchOverrides.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(WithInstalls((GameRoot, Overrides), (OtherRoot, GameLaunchOverrides.Empty)), CreateStore().Load());
    }

    [Fact]
    public async Task SaveInstallOverridesAsync_SameInstallAgain_ReplacesOnlyItsEntry()
    {
        var changed = Overrides with { JavaHome = null, MaxMemoryMb = null };
        await CreateStore().SaveInstallOverridesAsync(GameRoot, Overrides, TestContext.Current.CancellationToken);
        await CreateStore().SaveInstallOverridesAsync(OtherRoot, Overrides, TestContext.Current.CancellationToken);

        await CreateStore().SaveInstallOverridesAsync(GameRoot, changed, TestContext.Current.CancellationToken);

        Assert.Equal(WithInstalls((GameRoot, changed), (OtherRoot, Overrides)), CreateStore().Load());
    }

    [Fact]
    public async Task SaveInstallOverridesAsync_WritesInstallsObjectKeyedByRoot()
    {
        var store = CreateStore();
        await store.SaveInstallOverridesAsync(GameRoot, Overrides, TestContext.Current.CancellationToken);
        await store.SaveInstallOverridesAsync(OtherRoot, GameLaunchOverrides.Empty, TestContext.Current.CancellationToken);

        var game = $$"""{{JsonSerializer.Serialize(GameRoot)}}:{"javaHome":{{JsonSerializer.Serialize(JavaA)}},"maxMemoryMb":8192,"minMemoryMb":2048,"jvmArgs":"-Dzomboid.steam=1","gameArgs":"-debug","gc":"shenandoah"}""";
        var other = $$"""{{JsonSerializer.Serialize(OtherRoot)}}:{"jvmArgs":"","gameArgs":""}""";
        Assert.Equal(
            """{"installs":{""" + game + "," + other + "}}",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveLanguageAsync_WritesCultureName()
    {
        await CreateStore().SaveLanguageAsync(AppLanguage.SimplifiedChinese, TestContext.Current.CancellationToken);

        Assert.Equal("""{"language":"zh-Hans"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.Dark, "dark")]
    public async Task SaveThemeAsync_WritesStableName(AppTheme theme, string expected)
    {
        await CreateStore().SaveThemeAsync(theme, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"theme":"{{expected}}"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveWindowPlacementAsync_WritesWindowObject()
    {
        await CreateStore().SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"window":{"x":-1910,"y":30,"width":1000.5,"height":640,"maximized":true}}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveLanguageAsync_Twice_LastChoiceWins()
    {
        var store = CreateStore();

        await store.SaveLanguageAsync(AppLanguage.TraditionalChinese, TestContext.Current.CancellationToken);
        await store.SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);

        Assert.Equal(AppLanguage.English, store.Load().Language);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task SaveOneField_FileHoldsTheOthers_KeepsAll(int savedLast)
    {
        var ct = TestContext.Current.CancellationToken;
        Func<Task>[] saves =
        [
            () => CreateStore().SaveLanguageAsync(AppLanguage.Russian, ct),
            () => CreateStore().SaveThemeAsync(AppTheme.Dark, ct),
            () => CreateStore().SaveWindowPlacementAsync(Placement, ct),
            () => CreateStore().SaveManualJavaHomesAsync([JavaA], ct),
            () => CreateStore().SaveInstallOverridesAsync(GameRoot, Overrides, ct),
            () => CreateStore().SaveZombieBuddyChannelAsync(GameRoot, ZombieBuddyChannel.Beta, ct),
            () => CreateStore().SaveDefaultModCollectionAsync(GameRoot, "B42 pack", ct),
            () => CreateStore().SaveUpdateSourceAsync(UpdateSource.Gitee, ct),
            () => CreateStore().SaveAutoCheckUpdatesAsync(false, ct),
            () => CreateStore().SaveProxyAsync(ManualProxy, ct),
            () => CreateStore().SaveSteamHookReminderAsync(false, ct),
            () => CreateStore().SaveWorkshopCollectionsAsync([SteamCollection], ct),
            () => CreateStore().SaveWorkshopUserCollectionsAsync(UserCollections, ct),
            () => CreateStore().SaveWorkshopFavoritesAsync(Favorites, ct),
            () => CreateStore().SaveAfterLaunchAsync(AfterLaunchAction.MinimizeToTray, ct),
        ];

        // Separate stores, as across app runs.
        foreach (var save in saves.Where((_, i) => i != savedLast).Append(saves[savedLast]))
        {
            await save();
        }

        Assert.Equal(
            new UserPreferences(AppLanguage.Russian, AppTheme.Dark, Placement)
            {
                ManualJavaHomes = [JavaA],
                InstallOverrides = WithInstalls((GameRoot, Overrides)).InstallOverrides,
                ZombieBuddyChannels = WithChannels((GameRoot, ZombieBuddyChannel.Beta)).ZombieBuddyChannels,
                DefaultModCollections = WithDefaultCollections((GameRoot, "B42 pack")).DefaultModCollections,
                UpdateSource = UpdateSource.Gitee,
                AutoCheckUpdates = false,
                Proxy = ManualProxy,
                SteamHookReminder = false,
                WorkshopCollections = [SteamCollection],
                WorkshopUserCollections = UserCollections,
                WorkshopFavorites = Favorites,
                AfterLaunch = AfterLaunchAction.MinimizeToTray,
            },
            CreateStore().Load());
    }

    [Fact]
    public async Task SaveAllFields_Concurrently_KeepsAll()
    {
        var store = CreateStore();
        store.Load();

        await Task.WhenAll(
            store.SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken),
            store.SaveLanguageAsync(AppLanguage.PortugueseBrazil, TestContext.Current.CancellationToken),
            store.SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken),
            store.SaveManualJavaHomesAsync([JavaA, JavaB], TestContext.Current.CancellationToken),
            store.SaveInstallOverridesAsync(GameRoot, Overrides, TestContext.Current.CancellationToken),
            store.SaveInstallOverridesAsync(OtherRoot, GameLaunchOverrides.Empty, TestContext.Current.CancellationToken),
            store.SaveZombieBuddyChannelAsync(GameRoot, ZombieBuddyChannel.Beta, TestContext.Current.CancellationToken),
            store.SaveZombieBuddyChannelAsync(OtherRoot, ZombieBuddyChannel.Stable, TestContext.Current.CancellationToken),
            store.SaveDefaultModCollectionAsync(GameRoot, "B42 pack", TestContext.Current.CancellationToken),
            store.SaveDefaultModCollectionAsync(OtherRoot, "my collection", TestContext.Current.CancellationToken),
            store.SaveUpdateSourceAsync(UpdateSource.GitHub, TestContext.Current.CancellationToken),
            store.SaveAutoCheckUpdatesAsync(false, TestContext.Current.CancellationToken),
            store.SaveProxyAsync(ManualProxy, TestContext.Current.CancellationToken),
            store.SaveSteamHookReminderAsync(false, TestContext.Current.CancellationToken),
            store.SaveWorkshopCollectionsAsync([SteamCollection], TestContext.Current.CancellationToken),
            store.SaveWorkshopUserCollectionsAsync(UserCollections, TestContext.Current.CancellationToken),
            store.SaveWorkshopFavoritesAsync(Favorites, TestContext.Current.CancellationToken),
            store.SaveAfterLaunchAsync(AfterLaunchAction.Close, TestContext.Current.CancellationToken));

        Assert.Equal(
            new UserPreferences(AppLanguage.PortugueseBrazil, AppTheme.Dark, Placement)
            {
                ManualJavaHomes = [JavaA, JavaB],
                InstallOverrides = WithInstalls((GameRoot, Overrides), (OtherRoot, GameLaunchOverrides.Empty)).InstallOverrides,
                ZombieBuddyChannels = WithChannels((GameRoot, ZombieBuddyChannel.Beta), (OtherRoot, ZombieBuddyChannel.Stable)).ZombieBuddyChannels,
                DefaultModCollections = WithDefaultCollections((GameRoot, "B42 pack"), (OtherRoot, "my collection")).DefaultModCollections,
                UpdateSource = UpdateSource.GitHub,
                AutoCheckUpdates = false,
                Proxy = ManualProxy,
                SteamHookReminder = false,
                WorkshopCollections = [SteamCollection],
                WorkshopUserCollections = UserCollections,
                WorkshopFavorites = Favorites,
                AfterLaunch = AfterLaunchAction.Close,
            },
            CreateStore().Load());
    }

    [Fact]
    public async Task SaveAnyField_TargetUnwritable_DoesNotThrow()
    {
        Directory.CreateDirectory(FilePath);

        await CreateStore().SaveLanguageAsync(AppLanguage.English, TestContext.Current.CancellationToken);
        await CreateStore().SaveThemeAsync(AppTheme.Dark, TestContext.Current.CancellationToken);
        await CreateStore().SaveWindowPlacementAsync(Placement, TestContext.Current.CancellationToken);
        await CreateStore().SaveManualJavaHomesAsync([JavaA], TestContext.Current.CancellationToken);
        await CreateStore().SaveInstallOverridesAsync(GameRoot, Overrides, TestContext.Current.CancellationToken);
        await CreateStore().SaveZombieBuddyChannelAsync(GameRoot, ZombieBuddyChannel.Beta, TestContext.Current.CancellationToken);
        await CreateStore().SaveDefaultModCollectionAsync(GameRoot, "B42 pack", TestContext.Current.CancellationToken);
        await CreateStore().SaveUpdateSourceAsync(UpdateSource.Gitee, TestContext.Current.CancellationToken);
        await CreateStore().SaveAutoCheckUpdatesAsync(false, TestContext.Current.CancellationToken);
        await CreateStore().SaveProxyAsync(ManualProxy, TestContext.Current.CancellationToken);
        await CreateStore().SaveSteamHookReminderAsync(false, TestContext.Current.CancellationToken);
        await CreateStore().SaveWorkshopCollectionsAsync([SteamCollection], TestContext.Current.CancellationToken);
        await CreateStore().SaveWorkshopUserCollectionsAsync(UserCollections, TestContext.Current.CancellationToken);
        await CreateStore().SaveWorkshopFavoritesAsync(Favorites, TestContext.Current.CancellationToken);
        await CreateStore().SaveAfterLaunchAsync(AfterLaunchAction.Close, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsNoChoices() => Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());

    [Fact]
    public void Load_LanguageOnlyFileFromEarlierVersion_LoadsLanguageWithoutTheme()
    {
        WriteFile("""{"language":"ru"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_UnknownTheme_KeepsLanguageAndReturnsNoTheme()
    {
        WriteFile("""{"language":"ru","theme":"sepia"}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, null, null), CreateStore().Load());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"y":30,"width":1000,"height":640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":640}""")]
    [InlineData("""{"x":10,"y":30,"width":0,"height":640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":-640,"maximized":false}""")]
    [InlineData("""{"x":10,"y":30,"width":1000,"height":null,"maximized":false}""")]
    public void Load_InvalidWindowPlacement_KeepsOtherFieldsAndReturnsNoPlacement(string window)
    {
        WriteFile($$"""{"language":"ru","theme":"dark","window":{{window}}}""");

        Assert.Equal(new UserPreferences(AppLanguage.Russian, AppTheme.Dark, null), CreateStore().Load());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"language":5}""")]
    [InlineData("""{"language":"fr"}""")]
    [InlineData("""{"language":"es"}""")]
    [InlineData("""{"language":"pt"}""")]
    [InlineData("""{"language":"SimplifiedChinese"}""")]
    [InlineData("""{"theme":1}""")]
    [InlineData("""{"theme":"Default"}""")]
    [InlineData("""{"window":5}""")]
    [InlineData("""{"window":{"x":"left"}}""")]
    [InlineData("""{"window":{"x":10,"y":30,"width":NaN,"height":640,"maximized":false}}""")]
    [InlineData("""{"javaRuntimes":5}""")]
    [InlineData("""{"javaRuntimes":"/opt/jdk"}""")]
    [InlineData("""{"installs":5}""")]
    [InlineData("""{"installs":[]}""")]
    [InlineData("""{"zombieBuddyChannels":5}""")]
    [InlineData("""{"zombieBuddyChannels":["beta"]}""")]
    [InlineData("""{"defaultModCollections":5}""")]
    [InlineData("""{"defaultModCollections":["B42 pack"]}""")]
    public void Load_CorruptOrUnknownContent_ReturnsNoChoices(string content)
    {
        WriteFile(content);

        Assert.Equal(new UserPreferences(null, null, null), CreateStore().Load());
    }

    [Fact]
    public void Load_JavaRuntimesWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        var valid = System.Text.Json.JsonSerializer.Serialize(JavaA);
        WriteFile($$"""{"theme":"dark","javaRuntimes":[{{valid}},5,null,"relative/jdk","",{"path":"x"},{{valid}}]}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { ManualJavaHomes = [JavaA, JavaA] }, CreateStore().Load());
    }

    [Fact]
    public void Load_InstallsWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        string Key(string root) => JsonSerializer.Serialize(root);
        var java = JsonSerializer.Serialize(JavaA);
        var relativeRoot = JsonSerializer.Serialize("relative/ProjectZomboid");
        string Child(string name) => Key(Path.Combine(GameRoot, name));
        WriteFile($$"""
            {"theme":"dark","installs":{
              {{Key(GameRoot)}}:{"javaHome":{{java}},"maxMemoryMb":8192,"minMemoryMb":2048,"jvmArgs":"-Dzomboid.steam=1","gameArgs":"-debug","gc":"shenandoah"},
              {{Key(OtherRoot)}}:{},
              {{relativeRoot}}:{"gameArgs":"-debug"},
              {{Child("zero")}}:{"maxMemoryMb":0},
              {{Child("negative")}}:{"minMemoryMb":-512},
              {{Child("min-above-max")}}:{"maxMemoryMb":2048,"minMemoryMb":4096},
              {{Child("relative-java")}}:{"javaHome":"jdk-21"},
              {{Child("text-memory")}}:{"maxMemoryMb":"8g"},
              {{Child("fraction")}}:{"maxMemoryMb":1.5},
              {{Child("number-gc")}}:{"gc":1},
              {{Child("number")}}:5,
              {{Child("null")}}:null
            }
            }
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null)
            {
                InstallOverrides = WithInstalls((GameRoot, Overrides), (OtherRoot, GameLaunchOverrides.Empty)).InstallOverrides,
            },
            CreateStore().Load());
    }

    public static TheoryData<GarbageCollector> Collectors => [.. Enum.GetValues<GarbageCollector>()];

    [Theory]
    [MemberData(nameof(Collectors))]
    public async Task SaveInstallOverridesAsync_EveryGc_RoundTrips(GarbageCollector gc)
    {
        var overrides = GameLaunchOverrides.Empty with { GarbageCollector = gc };

        await CreateStore().SaveInstallOverridesAsync(GameRoot, overrides, TestContext.Current.CancellationToken);

        Assert.Equal(WithInstalls((GameRoot, overrides)), CreateStore().Load());
    }

    [Theory]
    [InlineData("g1", GarbageCollector.G1)]
    [InlineData("zgc", GarbageCollector.Z)]
    [InlineData("shenandoah", GarbageCollector.Shenandoah)]
    [InlineData("parallel", GarbageCollector.Parallel)]
    [InlineData("serial", GarbageCollector.Serial)]
    public async Task SaveInstallOverridesAsync_WritesStableGcNames(string name, GarbageCollector gc)
    {
        await CreateStore().SaveInstallOverridesAsync(GameRoot, GameLaunchOverrides.Empty with { GarbageCollector = gc }, TestContext.Current.CancellationToken);

        Assert.Contains($"\"gc\":\"{name}\"", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UnknownGc_KeepsTheEntryFollowingJson()
    {
        WriteFile("""{"installs":{""" + JsonSerializer.Serialize(GameRoot) + """:{"gameArgs":"-debug","gc":"epsilon"}}}""");

        Assert.Equal(WithInstalls((GameRoot, GameLaunchOverrides.Empty with { ExtraGameArgs = "-debug" })), CreateStore().Load());
    }

    [Fact]
    public async Task SaveZombieBuddyChannelAsync_ThenLoad_RoundTripsPerInstall()
    {
        var store = CreateStore();

        await store.SaveZombieBuddyChannelAsync(GameRoot, ZombieBuddyChannel.Beta, TestContext.Current.CancellationToken);
        await store.SaveZombieBuddyChannelAsync(OtherRoot, ZombieBuddyChannel.Stable, TestContext.Current.CancellationToken);
        await store.SaveZombieBuddyChannelAsync(GameRoot, ZombieBuddyChannel.Stable, TestContext.Current.CancellationToken);

        Assert.Equal(WithChannels((GameRoot, ZombieBuddyChannel.Stable), (OtherRoot, ZombieBuddyChannel.Stable)), CreateStore().Load());
    }

    [Theory]
    [InlineData(ZombieBuddyChannel.Stable, "stable")]
    [InlineData(ZombieBuddyChannel.Beta, "beta")]
    public async Task SaveZombieBuddyChannelAsync_WritesStableNamesKeyedByRoot(ZombieBuddyChannel channel, string name)
    {
        await CreateStore().SaveZombieBuddyChannelAsync(GameRoot, channel, TestContext.Current.CancellationToken);

        Assert.Equal(
            "{\"zombieBuddyChannels\":{" + JsonSerializer.Serialize(GameRoot) + ":\"" + name + "\"}}",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_ZombieBuddyChannelsWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        string Key(string root) => JsonSerializer.Serialize(root);
        WriteFile($$"""
            {"theme":"dark","zombieBuddyChannels":{
              {{Key(GameRoot)}}:"beta",
              {{Key(OtherRoot)}}:"stable",
              {{Key("relative/ProjectZomboid")}}:"beta",
              {{Key(Path.Combine(GameRoot, "unknown"))}}:"nightly",
              {{Key(Path.Combine(GameRoot, "number"))}}:1,
              {{Key(Path.Combine(GameRoot, "null"))}}:null,
              {{Key(Path.Combine(GameRoot, "object"))}}:{"channel":"beta"}
            }
            }
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null)
            {
                ZombieBuddyChannels = WithChannels((GameRoot, ZombieBuddyChannel.Beta), (OtherRoot, ZombieBuddyChannel.Stable)).ZombieBuddyChannels,
            },
            CreateStore().Load());
    }

    [Fact]
    public async Task SaveDefaultModCollectionAsync_ThenLoad_RoundTripsPerInstall()
    {
        var store = CreateStore();

        await store.SaveDefaultModCollectionAsync(GameRoot, "B42 通用合集", TestContext.Current.CancellationToken);
        await store.SaveDefaultModCollectionAsync(OtherRoot, "my collection", TestContext.Current.CancellationToken);
        await store.SaveDefaultModCollectionAsync(GameRoot, "Vanilla+", TestContext.Current.CancellationToken);

        Assert.Equal(WithDefaultCollections((GameRoot, "Vanilla+"), (OtherRoot, "my collection")), CreateStore().Load());
    }

    [Fact]
    public async Task SaveDefaultModCollectionAsync_Null_RemovesOnlyThatInstallsEntry()
    {
        await CreateStore().SaveDefaultModCollectionAsync(GameRoot, "B42 pack", TestContext.Current.CancellationToken);
        await CreateStore().SaveDefaultModCollectionAsync(OtherRoot, "my collection", TestContext.Current.CancellationToken);

        await CreateStore().SaveDefaultModCollectionAsync(GameRoot, null, TestContext.Current.CancellationToken);

        Assert.Equal(WithDefaultCollections((OtherRoot, "my collection")), CreateStore().Load());
    }

    [Fact]
    public async Task SaveDefaultModCollectionAsync_WritesNamesKeyedByRoot()
    {
        await CreateStore().SaveDefaultModCollectionAsync(GameRoot, "B42 pack", TestContext.Current.CancellationToken);

        Assert.Equal(
            "{\"defaultModCollections\":{" + JsonSerializer.Serialize(GameRoot) + ":\"B42 pack\"}}",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_DefaultModCollectionsWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        string Key(string root) => JsonSerializer.Serialize(root);
        WriteFile($$"""
            {"theme":"dark","defaultModCollections":{
              {{Key(GameRoot)}}:"B42 pack",
              {{Key(OtherRoot)}}:"my collection",
              {{Key("relative/ProjectZomboid")}}:"B42 pack",
              {{Key(Path.Combine(GameRoot, "empty"))}}:"",
              {{Key(Path.Combine(GameRoot, "number"))}}:1,
              {{Key(Path.Combine(GameRoot, "null"))}}:null,
              {{Key(Path.Combine(GameRoot, "object"))}}:{"name":"B42 pack"}
            }
            }
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null)
            {
                DefaultModCollections = WithDefaultCollections((GameRoot, "B42 pack"), (OtherRoot, "my collection")).DefaultModCollections,
            },
            CreateStore().Load());
    }

    public static TheoryData<UpdateSource> UpdateSources => [.. Enum.GetValues<UpdateSource>()];

    [Theory]
    [MemberData(nameof(UpdateSources))]
    public async Task SaveUpdateSourceAsync_ThenLoad_RoundTrips(UpdateSource source)
    {
        await CreateStore().SaveUpdateSourceAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { UpdateSource = source }, CreateStore().Load());
    }

    [Theory]
    [InlineData(UpdateSource.Auto, "auto")]
    [InlineData(UpdateSource.GitHub, "github")]
    [InlineData(UpdateSource.Gitee, "gitee")]
    public async Task SaveUpdateSourceAsync_WritesStableName(UpdateSource source, string expected)
    {
        await CreateStore().SaveUpdateSourceAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"updateSource":"{{expected}}"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAutoCheckUpdatesAsync_ThenLoad_RoundTrips(bool enabled)
    {
        await CreateStore().SaveAutoCheckUpdatesAsync(enabled, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { AutoCheckUpdates = enabled }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveAutoCheckUpdatesAsync_WritesBool()
    {
        await CreateStore().SaveAutoCheckUpdatesAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal("""{"autoCheckUpdates":false}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoUpdateFields_LoadsNullForBoth()
    {
        WriteFile("""{"theme":"dark"}""");

        var loaded = CreateStore().Load();

        Assert.Null(loaded.UpdateSource);
        Assert.Null(loaded.AutoCheckUpdates);
    }

    [Theory]
    [InlineData("\"bitbucket\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[\"gitee\"]")]
    public void Load_UnknownUpdateSource_KeepsOtherFieldsAndReturnsNoSource(string value)
    {
        WriteFile($$"""{"theme":"dark","updateSource":{{value}},"autoCheckUpdates":false}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { AutoCheckUpdates = false }, CreateStore().Load());
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Load_NonBoolAutoCheckUpdates_KeepsOtherFieldsAndReturnsNoChoice(string value)
    {
        WriteFile($$"""{"theme":"dark","updateSource":"gitee","autoCheckUpdates":{{value}}}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { UpdateSource = UpdateSource.Gitee }, CreateStore().Load());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveSteamHookReminderAsync_ThenLoad_RoundTrips(bool remind)
    {
        await CreateStore().SaveSteamHookReminderAsync(remind, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { SteamHookReminder = remind }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveSteamHookReminderAsync_WritesBool()
    {
        await CreateStore().SaveSteamHookReminderAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal("""{"steamHookReminder":false}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoSteamHookReminder_LoadsNull()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Null(CreateStore().Load().SteamHookReminder);
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Load_NonBoolSteamHookReminder_KeepsOtherFieldsAndReturnsNoChoice(string value)
    {
        WriteFile($$"""{"theme":"dark","steamHookReminder":{{value}},"autoCheckUpdates":false}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { AutoCheckUpdates = false }, CreateStore().Load());
    }

    public static TheoryData<AfterLaunchAction> AfterLaunchActions => [.. Enum.GetValues<AfterLaunchAction>()];

    [Theory]
    [MemberData(nameof(AfterLaunchActions))]
    public async Task SaveAfterLaunchAsync_ThenLoad_RoundTrips(AfterLaunchAction action)
    {
        await CreateStore().SaveAfterLaunchAsync(action, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { AfterLaunch = action }, CreateStore().Load());
    }

    [Theory]
    [InlineData(AfterLaunchAction.Keep, "keep")]
    [InlineData(AfterLaunchAction.Minimize, "minimize")]
    [InlineData(AfterLaunchAction.MinimizeToTray, "tray")]
    [InlineData(AfterLaunchAction.Close, "close")]
    public async Task SaveAfterLaunchAsync_WritesStableName(AfterLaunchAction action, string expected)
    {
        await CreateStore().SaveAfterLaunchAsync(action, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"afterLaunch":"{{expected}}"}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoAfterLaunch_LoadsNull()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Null(CreateStore().Load().AfterLaunch);
    }

    [Theory]
    [InlineData("\"hide\"")]
    [InlineData("\"MinimizeToTray\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[\"close\"]")]
    public void Load_UnknownAfterLaunch_KeepsOtherFieldsAndReturnsNoChoice(string value)
    {
        WriteFile($$"""{"theme":"dark","afterLaunch":{{value}},"autoCheckUpdates":false}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { AutoCheckUpdates = false }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopCollectionsAsync_ThenLoad_RoundTripsInOrder()
    {
        var untitled = new SavedWorkshopEntry(2002, null);

        await CreateStore().SaveWorkshopCollectionsAsync([untitled, SteamCollection], TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { WorkshopCollections = [untitled, SteamCollection] }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopCollectionsAsync_WritesIdsAsTextAndOmitsUnknownTitles()
    {
        await CreateStore().SaveWorkshopCollectionsAsync(
            [new SavedWorkshopEntry(18446744073709551615, "B42 pack"), new SavedWorkshopEntry(2002, null)],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"workshopCollections":[{"id":"18446744073709551615","title":"B42 pack"},{"id":"2002"}]}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoWorkshopCollections_LoadsNone()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Empty(CreateStore().Load().WorkshopCollections);
    }

    [Fact]
    public void Load_WorkshopCollectionsWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        WriteFile("""
            {"theme":"dark","workshopCollections":[
              {"id":"1001","title":"First"},
              {"id":"1002"},
              {"id":"1002","title":"Repeated"},
              {"id":1003,"title":"Number id"},
              {"id":"0","title":"Zero"},
              {"id":"-5","title":"Negative"},
              {"id":"abc","title":"Text"},
              {"title":"No id"},
              {"id":"1004","title":7},
              {"id":"1005","title":""},
              "1006",
              null
            ]}
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null) { WorkshopCollections = [new(1001, "First"), new(1002, null), new(1005, null)] },
            CreateStore().Load());
    }

    [Theory]
    [InlineData("\"1001\"")]
    [InlineData("{}")]
    [InlineData("5")]
    public void Load_WorkshopCollectionsNotAnArray_KeepsOtherFieldsAndLoadsNone(string value)
    {
        WriteFile($$"""{"theme":"dark","workshopCollections":{{value}}}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null), CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopUserCollectionsAsync_ThenLoad_RoundTrips()
    {
        await CreateStore().SaveWorkshopUserCollectionsAsync(UserCollections, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { WorkshopUserCollections = UserCollections }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopUserCollectionsAsync_WritesIdsAsTextAndOmitsUnknownTitles()
    {
        var collections = new SavedWorkshopUserCollections(
            new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero),
            new WorkshopUserCollections(12345678, [new WorkshopCollectionSummary(18446744073709551615, "pack", 201)], [new WorkshopCollectionSummary(2002, null, 7)]));

        await CreateStore().SaveWorkshopUserCollectionsAsync(collections, TestContext.Current.CancellationToken);

        // The default JSON encoder escapes '+'.
        Assert.Equal(
            """{"workshopUserCollections":{"accountId":12345678,"fetchedAt":"2026-10-19T08:30:00\u002B00:00","favorited":[{"id":"18446744073709551615","title":"pack","childCount":201}],"published":[{"id":"2002","childCount":7}]}}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoWorkshopUserCollections_LoadsNull()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Null(CreateStore().Load().WorkshopUserCollections);
    }

    [Fact]
    public void Load_WorkshopUserCollectionsWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        WriteFile("""
            {"theme":"dark","workshopUserCollections":{"accountId":42,"fetchedAt":"2026-10-19T08:30:00+00:00","favorited":[
              {"id":"1001","title":"First","childCount":3},
              {"id":"1002","childCount":0},
              {"id":"1002","title":"Repeated","childCount":1},
              {"id":1003,"title":"Number id","childCount":1},
              {"id":"0","title":"Zero","childCount":1},
              {"id":"abc","title":"Text","childCount":1},
              {"title":"No id","childCount":1},
              {"id":"1004","title":7,"childCount":1},
              {"id":"1005","title":"No count"},
              {"id":"1006","title":"Negative count","childCount":-1},
              {"id":"1007","title":"Text count","childCount":"5"},
              {"id":"1008","title":"","childCount":2},
              "1009",
              null
            ],"published":[{"id":"1001","title":"Also favorited","childCount":3}]}}
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null)
            {
                WorkshopUserCollections = new SavedWorkshopUserCollections(
                    new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero),
                    new WorkshopUserCollections(
                        42,
                        [new WorkshopCollectionSummary(1001, "First", 3), new WorkshopCollectionSummary(1002, null, 0), new WorkshopCollectionSummary(1008, null, 2)],
                        [new WorkshopCollectionSummary(1001, "Also favorited", 3)])),
            },
            CreateStore().Load());
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("""{"fetchedAt":"2026-10-19T08:30:00+00:00","favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":0,"fetchedAt":"2026-10-19T08:30:00+00:00","favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":-1,"fetchedAt":"2026-10-19T08:30:00+00:00","favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":"42","fetchedAt":"2026-10-19T08:30:00+00:00","favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":42,"favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":42,"fetchedAt":"yesterday","favorited":[],"published":[]}""")]
    [InlineData("""{"accountId":42,"fetchedAt":"2026-10-19T08:30:00+00:00","published":[]}""")]
    [InlineData("""{"accountId":42,"fetchedAt":"2026-10-19T08:30:00+00:00","favorited":{},"published":[]}""")]
    public void Load_InvalidWorkshopUserCollections_KeepsOtherFieldsAndLoadsNull(string value)
    {
        WriteFile($$"""{"theme":"dark","workshopUserCollections":{{value}}}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null), CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopFavoritesAsync_ThenLoad_RoundTripsInOrder()
    {
        await CreateStore().SaveWorkshopFavoritesAsync(Favorites, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { WorkshopFavorites = Favorites }, CreateStore().Load());
    }

    [Fact]
    public async Task SaveWorkshopFavoritesAsync_WritesIdsAsTextAndOmitsUnknownTitles()
    {
        await CreateStore().SaveWorkshopFavoritesAsync(
            new SavedWorkshopFavorites([new SavedWorkshopEntry(18446744073709551615, "pack")], [new SavedWorkshopEntry(2002, null)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"workshopFavorites":{"collections":[{"id":"18446744073709551615","title":"pack"}],"items":[{"id":"2002"}]}}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Load_NoWorkshopFavorites_LoadsNone()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Equal(SavedWorkshopFavorites.None, CreateStore().Load().WorkshopFavorites);
    }

    [Fact]
    public void Load_WorkshopFavoritesWithInvalidEntries_KeepsTheValidOnesAndOtherFields()
    {
        WriteFile("""
            {"theme":"dark","workshopFavorites":{"collections":[
              {"id":"1001","title":"First"},
              {"id":"1001","title":"Repeated"},
              {"id":1002,"title":"Number id"},
              {"id":"0","title":"Zero"},
              {"id":"abc","title":"Text"},
              {"title":"No id"},
              {"id":"1003","title":7},
              {"id":"1004","title":""},
              "1005",
              null
            ],"items":[
              {"id":"1001","title":"Same id as a collection"},
              {"id":"2001"},
              {"id":"2001","title":"Repeated"},
              {"id":"-5"}
            ]}}
            """);

        Assert.Equal(
            new UserPreferences(null, AppTheme.Dark, null)
            {
                WorkshopFavorites = new SavedWorkshopFavorites(
                    [new SavedWorkshopEntry(1001, "First"), new SavedWorkshopEntry(1004, null)],
                    [new SavedWorkshopEntry(1001, "Same id as a collection"), new SavedWorkshopEntry(2001, null)]),
            },
            CreateStore().Load());
    }

    [Theory]
    [InlineData("""{"items":[{"id":"2001"}]}""")]
    [InlineData("""{"collections":{},"items":[{"id":"2001"}]}""")]
    [InlineData("""{"collections":"1001","items":[{"id":"2001"}]}""")]
    public void Load_WorkshopFavoritesListMissingOrNotAnArray_LoadsItEmptyAndKeepsTheOther(string value)
    {
        WriteFile($$"""{"workshopFavorites":{{value}}}""");

        Assert.Equal(new SavedWorkshopFavorites([], [new SavedWorkshopEntry(2001, null)]), CreateStore().Load().WorkshopFavorites);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    public void Load_WorkshopFavoritesNotAnObject_KeepsOtherFieldsAndLoadsNone(string value)
    {
        WriteFile($$"""{"theme":"dark","workshopFavorites":{{value}}}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null), CreateStore().Load());
    }

    public static TheoryData<SavedProxy> Proxies =>
    [
        ManualProxy,
        new SavedProxy(ProxyMode.System, null, null, null, null),
        new SavedProxy(ProxyMode.None, "proxy.example.test", 3128, null, null),
        new SavedProxy(ProxyMode.Manual, "::1", 1, "bob", null),
    ];

    [Theory]
    [MemberData(nameof(Proxies))]
    public async Task SaveProxyAsync_ThenLoad_RoundTrips(SavedProxy proxy)
    {
        await CreateStore().SaveProxyAsync(proxy, TestContext.Current.CancellationToken);

        Assert.Equal(new UserPreferences(null, null, null) { Proxy = proxy }, CreateStore().Load());
    }

    [Theory]
    [InlineData(ProxyMode.System, "system")]
    [InlineData(ProxyMode.None, "none")]
    [InlineData(ProxyMode.Manual, "manual")]
    public async Task SaveProxyAsync_WritesStableModeNames(ProxyMode mode, string expected)
    {
        await CreateStore().SaveProxyAsync(new SavedProxy(mode, null, null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal($$$"""{"proxy":{"mode":"{{{expected}}}"}}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveProxyAsync_PlainProtector_StoresThePasswordAsPlainText()
    {
        await CreateStore().SaveProxyAsync(ManualProxy with { Password = "secret" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"proxy":{"mode":"manual","host":"127.0.0.1","port":7890,"username":"alice","password":"plain:secret"}}""",
            await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveProxyAsync_EmptyFields_AreOmitted()
    {
        await CreateStore().SaveProxyAsync(new SavedProxy(ProxyMode.Manual, "", 8080, "", ""), TestContext.Current.CancellationToken);

        Assert.Equal("""{"proxy":{"mode":"manual","port":8080}}""", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
        Assert.Equal(new SavedProxy(ProxyMode.Manual, null, 8080, null, null), CreateStore().Load().Proxy);
    }

    [Fact]
    public async Task SaveProxyAsync_Dpapi_RoundTripsWithoutThePlainTextInTheFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var dpapi = new WindowsDpapiSecretProtector();

        await CreateStore(dpapi).SaveProxyAsync(ManualProxy, TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken);
        Assert.Contains("\"password\":\"dpapi:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", text, StringComparison.Ordinal);
        Assert.Equal(ManualProxy, CreateStore(dpapi).Load().Proxy);
    }

    [Fact]
    public void Load_NoProxy_LoadsNull()
    {
        WriteFile("""{"theme":"dark"}""");

        Assert.Null(CreateStore().Load().Proxy);
    }

    [Theory]
    [InlineData("\"manual\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"mode":"socks5","host":"127.0.0.1","port":1080}""")]
    [InlineData("""{"mode":"","host":"127.0.0.1","port":1080}""")]
    [InlineData("""{"mode":5}""")]
    [InlineData("""{"mode":"manual","port":"7890"}""")]
    public void Load_UnknownProxy_KeepsOtherFieldsAndReturnsNoProxy(string value)
    {
        WriteFile($$"""{"theme":"dark","proxy":{{value}},"autoCheckUpdates":false}""");

        Assert.Equal(new UserPreferences(null, AppTheme.Dark, null) { AutoCheckUpdates = false }, CreateStore().Load());
    }

    [Theory]
    [InlineData("bad host", 7890, null, 7890)]
    [InlineData("127.0.0.1", 0, "127.0.0.1", null)]
    [InlineData("127.0.0.1", 65536, "127.0.0.1", null)]
    public void Load_InvalidHostOrPort_DropsOnlyThat(string host, int port, string? expectedHost, int? expectedPort)
    {
        WriteFile($$$"""{"proxy":{"mode":"none","host":"{{{host}}}","port":{{{port}}},"username":"alice"}}""");

        Assert.Equal(new SavedProxy(ProxyMode.None, expectedHost, expectedPort, "alice", null), CreateStore().Load().Proxy);
    }

    [Theory]
    [InlineData("dpapi:AQAAANCMnd8BFdERjHoAwE")]
    [InlineData("secret")]
    public void Load_PasswordOfAnotherScheme_LoadsAsEmptyAndKeepsTheRest(string password)
    {
        WriteFile($$$"""{"proxy":{"mode":"manual","host":"127.0.0.1","port":7890,"username":"alice","password":"{{{password}}}"}}""");

        Assert.Equal(new SavedProxy(ProxyMode.Manual, "127.0.0.1", 7890, "alice", null), CreateStore().Load().Proxy);
    }

    [Theory]
    [InlineData("dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA")]
    [InlineData("dpapi:not base64!")]
    [InlineData("plain:secret")]
    public void Load_UndecryptablePassword_LoadsAsEmptyAndKeepsTheRest(string password)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        WriteFile($$$"""{"proxy":{"mode":"manual","host":"127.0.0.1","port":7890,"username":"alice","password":"{{{password}}}"}}""");

        Assert.Equal(
            new SavedProxy(ProxyMode.Manual, "127.0.0.1", 7890, "alice", null),
            CreateStore(new WindowsDpapiSecretProtector()).Load().Proxy);
    }
}
