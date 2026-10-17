using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Tests.Services;

public sealed class JsonPreferenceStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static readonly WindowPlacement Placement = new(-1910, 30, 1000.5, 640, IsMaximized: true);

    private static readonly string JavaA = OperatingSystem.IsWindows() ? @"C:\Java\jdk-21" : "/opt/java/jdk-21";

    private static readonly string JavaB = OperatingSystem.IsWindows() ? @"D:\Tools\jre 17" : "/home/player/jre 17";

    private static readonly string GameRoot =
        OperatingSystem.IsWindows() ? @"E:\SteamLibrary\steamapps\common\ProjectZomboid" : "/home/player/.local/share/Steam/steamapps/common/ProjectZomboid";

    private static readonly string OtherRoot = OperatingSystem.IsWindows() ? @"D:\Games\Project Zomboid" : "/opt/games/Project Zomboid";

    private static readonly GameLaunchOverrides Overrides = new(JavaA, 8192, 2048, "-Dzomboid.steam=1", "-debug", GarbageCollector.Shenandoah);

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

    private JsonPreferenceStore CreateStore() => new(FilePath, NullLogger.Instance);

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
            store.SaveDefaultModCollectionAsync(OtherRoot, "my collection", TestContext.Current.CancellationToken));

        Assert.Equal(
            new UserPreferences(AppLanguage.PortugueseBrazil, AppTheme.Dark, Placement)
            {
                ManualJavaHomes = [JavaA, JavaB],
                InstallOverrides = WithInstalls((GameRoot, Overrides), (OtherRoot, GameLaunchOverrides.Empty)).InstallOverrides,
                ZombieBuddyChannels = WithChannels((GameRoot, ZombieBuddyChannel.Beta), (OtherRoot, ZombieBuddyChannel.Stable)).ZombieBuddyChannels,
                DefaultModCollections = WithDefaultCollections((GameRoot, "B42 pack"), (OtherRoot, "my collection")).DefaultModCollections,
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
}
