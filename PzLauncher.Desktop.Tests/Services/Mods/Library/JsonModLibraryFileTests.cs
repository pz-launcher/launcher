using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods.Library;

public sealed class JsonModLibraryFileTests : IDisposable
{
    private const string KeptAside = "mod-library.unreadable-20260101000000.json";

    private static readonly string GameRoot =
        OperatingSystem.IsWindows() ? @"E:\SteamLibrary\steamapps\common\ProjectZomboid" : "/home/player/.local/share/Steam/steamapps/common/ProjectZomboid";

    private static readonly string OtherRoot = OperatingSystem.IsWindows() ? @"D:\Games\Project Zomboid" : "/opt/games/Project Zomboid";

    private static readonly SavedLocalCollection LocalCollection = new(
        new Guid("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b"),
        "服务器 \"B42\"",
        [new SavedWorkshopEntry(18446744073709551615, "物品 \"one\""), new SavedWorkshopEntry(2002, null)]);

    private static readonly SavedModpack SteamModpack = new(
        "服务器 \"B42\"",
        new SavedModpackPreset("Survival", ["Core", "Maps"], IsLinked: true),
        new SavedModpackCollection.Steam(18446744073709551615),
        [2002, 18446744073709551614]);

    private static readonly SavedModpack LocalModpack = new(
        "Night",
        new SavedModpackPreset("Old", ["Cars"], IsLinked: false),
        new SavedModpackCollection.Local(LocalCollection.Id, "List", [new SavedWorkshopEntry(2002, "物品"), new SavedWorkshopEntry(2003, null)]),
        []);

    private readonly TempDirectory _temp = new();

    // In a folder that doesn't exist yet, as on first launch.
    private string FilePath => Path.Combine(_temp.Path, "PzLauncher", "mod-library.json");

    private string KeptAsidePath => Path.Combine(_temp.Path, "PzLauncher", KeptAside);

    private JsonModLibraryFile Create(TimeProvider? time = null) => new(FilePath, NullLogger.Instance, time ?? new ManualTimeProvider());

    private static Dictionary<string, IReadOnlyList<SavedModpack>> Modpacks(params (string Root, SavedModpack[] Modpacks)[] installs) =>
        installs.ToDictionary(install => install.Root, IReadOnlyList<SavedModpack> (install) => install.Modpacks);

    private static void AssertLoaded(ModLibrary expected, ModLibrary actual)
    {
        Assert.Equal(expected.LocalCollections, actual.LocalCollections);
        Assert.Equal(expected.Modpacks.Keys, actual.Modpacks.Keys);
        foreach (var (root, modpacks) in expected.Modpacks)
        {
            Assert.Equal(modpacks, actual.Modpacks[root]);
        }

        Assert.Equal(expected.LocalCollectionsComplete, actual.LocalCollectionsComplete);
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    private string ReadFile() => File.ReadAllText(FilePath);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_MissingFile_LoadsNothingAndIsComplete() => AssertLoaded(ModLibrary.Empty, Create().Load());

    [Fact]
    public async Task SaveBoth_ThenLoad_RoundTrips()
    {
        var file = Create();
        file.Load();

        await Task.WhenAll(
            file.SaveLocalCollectionsAsync([LocalCollection], TestContext.Current.CancellationToken),
            file.SaveModpacksAsync(Modpacks((GameRoot, [SteamModpack, LocalModpack]), (OtherRoot, [LocalModpack])), TestContext.Current.CancellationToken));

        AssertLoaded(
            new ModLibrary([LocalCollection], Modpacks((GameRoot, [SteamModpack, LocalModpack]), (OtherRoot, [LocalModpack])), LocalCollectionsComplete: true),
            Create().Load());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SaveOneField_FileHoldsTheOther_KeepsBoth(int savedLast)
    {
        var ct = TestContext.Current.CancellationToken;
        Func<Task>[] saves =
        [
            () => Create().SaveLocalCollectionsAsync([LocalCollection], ct),
            () => Create().SaveModpacksAsync(Modpacks((GameRoot, [SteamModpack])), ct),
        ];

        // Separate instances, as across app runs.
        await saves[1 - savedLast]();
        await saves[savedLast]();

        AssertLoaded(new ModLibrary([LocalCollection], Modpacks((GameRoot, [SteamModpack])), LocalCollectionsComplete: true), Create().Load());
    }

    [Fact]
    public async Task SaveModpacksAsync_WritesTheLinkOnlyWhenLinkedAndIdsAsText()
    {
        var independent = LocalModpack with { Collection = new SavedModpackCollection.Local(null, "List", [new SavedWorkshopEntry(2002, "Item")]) };

        await Create().SaveModpacksAsync(Modpacks((GameRoot, [independent, SteamModpack with { Name = "Pack" }])), TestContext.Current.CancellationToken);

        Assert.Equal(
            "{\"modpacks\":{" + JsonSerializer.Serialize(GameRoot) + ":["
            + """{"name":"Night","preset":{"copy":{"name":"Old","modIds":["Cars"]}},"collection":{"local":{"copy":{"name":"List","items":[{"id":"2002","title":"Item"}]}}},"extraItems":[]},"""
            + """{"name":"Pack","preset":{"linked":"Survival","copy":{"name":"Survival","modIds":["Core","Maps"]}},"collection":{"steam":"18446744073709551615"},"extraItems":["2002","18446744073709551614"]}"""
            + "]}}",
            ReadFile());
    }

    [Fact]
    public async Task SaveModpacksAsync_InstallLeftOut_HasNoEntry()
    {
        await Create().SaveModpacksAsync(Modpacks((GameRoot, [SteamModpack]), (OtherRoot, [LocalModpack])), TestContext.Current.CancellationToken);

        await Create().SaveModpacksAsync(Modpacks((OtherRoot, [LocalModpack])), TestContext.Current.CancellationToken);

        AssertLoaded(new ModLibrary([], Modpacks((OtherRoot, [LocalModpack])), LocalCollectionsComplete: true), Create().Load());
    }

    [Fact]
    public void Load_LocalCollectionsWithInvalidEntries_KeepsTheValidOnesAndIsIncomplete()
    {
        WriteFile("""
            {"localCollections":[
              {"id":"6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"List","items":[{"id":"2002","title":"Item"},{"id":"abc"},{"id":"2002"},{"id":"2003"}]},
              {"id":"6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"Repeated id","items":[]},
              {"id":"0b0c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"List","items":[]},
              {"id":"not a guid","name":"Bad id","items":[]},
              {"id":"00000000-0000-0000-0000-000000000000","name":"Empty id","items":[]},
              {"id":"1a1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":" ","items":[]},
              {"id":"2a1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"Two\nlines","items":[]},
              {"id":"3a1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"No items"},
              {"id":"4a1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b","name":"Empty","items":[]},
              5
            ]}
            """);

        AssertLoaded(
            new ModLibrary(
                [
                    new SavedLocalCollection(
                        new Guid("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b"), "List", [new SavedWorkshopEntry(2002, "Item"), new SavedWorkshopEntry(2003, null)]),
                    new SavedLocalCollection(new Guid("4a1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b"), "Empty", []),
                ],
                Modpacks(),
                LocalCollectionsComplete: false),
            Create().Load());
    }

    [Theory]
    [InlineData("5")]
    [InlineData("{}")]
    [InlineData("\"x\"")]
    public void Load_LocalCollectionsNotAnArray_LoadsNoneIsIncompleteAndKeepsModpacks(string value)
    {
        WriteFile("{\"localCollections\":" + value + ",\"modpacks\":{" + JsonSerializer.Serialize(GameRoot)
            + """:[{"name":"Pack","preset":{"copy":{"name":"P","modIds":[]}},"collection":{"steam":"9000"}}]}}""");

        var loaded = Create().Load();

        Assert.Empty(loaded.LocalCollections);
        Assert.False(loaded.LocalCollectionsComplete);
        Assert.Equal(["Pack"], loaded.Modpacks[GameRoot].Select(modpack => modpack.Name));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"localCollections":null}""")]
    [InlineData("""{"localCollections":[],"modpacks":5}""")]
    public void Load_NoLocalCollections_IsComplete(string content)
    {
        WriteFile(content);

        AssertLoaded(ModLibrary.Empty, Create().Load());
    }

    [Fact]
    public void Load_ModpacksWithInvalidEntries_KeepsTheValidOnes()
    {
        string Key(string root) => JsonSerializer.Serialize(root);
        const string Preset = """{"copy":{"name":"P","modIds":[]}}""";
        WriteFile("""
            {"modpacks":{
              GAME:[
                {"name":"Linked","preset":{"linked":"Survival","copy":{"name":"Survival","modIds":["Core",5,"","Maps"]}},"collection":{"steam":"9000"},"extraItems":["2002","abc",2003,"2002","2004"]},
                {"name":"Linked","preset":PRESET,"collection":{"steam":"9001"}},
                {"name":"Own","preset":{"copy":{"name":"Old","modIds":["Cars"]}},"collection":{"local":{"copy":{"name":"List","items":[{"id":"2002","title":"Item"},{"id":"x"}]}}}},
                {"name":" ","preset":PRESET,"collection":{"steam":"9000"}},
                {"name":"No preset","collection":{"steam":"9000"}},
                {"name":"Bad preset name","preset":{"copy":{"name":"a:b","modIds":[]}},"collection":{"steam":"9000"}},
                {"name":"No collection","preset":PRESET},
                {"name":"Both collections","preset":PRESET,"collection":{"steam":"9000","local":{"copy":{"name":"L","items":[]}}}},
                {"name":"Bad Steam id","preset":PRESET,"collection":{"steam":"abc"}},
                {"name":"Bad link","preset":PRESET,"collection":{"local":{"linked":"nope","copy":{"name":"L","items":[]}}}},
                5
              ],
              OTHER:5,
              RELATIVE:[{"name":"Relative","preset":PRESET,"collection":{"steam":"9000"}}]
            }}
            """
            .Replace("PRESET", Preset)
            .Replace("GAME", Key(GameRoot))
            .Replace("OTHER", Key(OtherRoot))
            .Replace("RELATIVE", Key("relative/ProjectZomboid")));

        AssertLoaded(
            new ModLibrary(
                [],
                Modpacks((
                    GameRoot,
                    [
                        new SavedModpack("Linked", new SavedModpackPreset("Survival", ["Core", "Maps"], IsLinked: true), new SavedModpackCollection.Steam(9000), [2002, 2004]),
                        new SavedModpack(
                            "Own",
                            new SavedModpackPreset("Old", ["Cars"], IsLinked: false),
                            new SavedModpackCollection.Local(null, "List", [new SavedWorkshopEntry(2002, "Item")]),
                            []),
                    ])),
                LocalCollectionsComplete: true),
            Create().Load());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Load_UnreadableFile_LoadsNothingIsIncompleteAndKeepsACopyBeforeTheFirstSave(string content)
    {
        WriteFile(content);
        var file = Create();

        AssertLoaded(ModLibrary.Empty with { LocalCollectionsComplete = false }, file.Load());
        Assert.False(File.Exists(KeptAsidePath));

        await file.SaveLocalCollectionsAsync([LocalCollection], TestContext.Current.CancellationToken);
        await file.SaveLocalCollectionsAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(content, await File.ReadAllTextAsync(KeptAsidePath, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Length);
        Assert.Equal("""{"localCollections":[]}""", ReadFile());
    }

    [Fact]
    public async Task Save_FileHasUnknownFields_WritesThemBackUnchanged()
    {
        WriteFile("""{"future":{"list":[1,"two",null]},"localCollections":[],"modpacks":{}}""");

        await Create().SaveLocalCollectionsAsync([], TestContext.Current.CancellationToken);

        Assert.Equal("""{"localCollections":[],"modpacks":{},"future":{"list":[1,"two",null]}}""", ReadFile());
    }

    [Fact]
    public async Task Save_TargetUnwritable_DoesNotThrow()
    {
        Directory.CreateDirectory(FilePath);

        await Create().SaveLocalCollectionsAsync([LocalCollection], TestContext.Current.CancellationToken);
        await Create().SaveModpacksAsync(Modpacks((GameRoot, [SteamModpack])), TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(FilePath));
    }

    [Fact]
    public async Task Save_WhileAWriteRuns_LaterSavesShareOneWriteOfTheLastState()
    {
        WriteFile("{");
        var clock = new HeldClock();
        var file = Create(clock);
        file.Load();

        // The first write holds in the copy-aside, after it took the file.
        var first = Task.Run(() => file.SaveLocalCollectionsAsync([LocalCollection], TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        // No token, so a save hands back the shared write itself.
        var second = file.SaveLocalCollectionsAsync([], CancellationToken.None);
        var third = file.SaveModpacksAsync(Modpacks((GameRoot, [SteamModpack])), CancellationToken.None);
        var fourth = file.SaveLocalCollectionsAsync([LocalCollection], CancellationToken.None);
        clock.Release.Set();
        await Task.WhenAll(first, second, third, fourth);

        Assert.Same(second, third);
        Assert.Same(second, fourth);
        Assert.Equal(1, clock.Calls);
        AssertLoaded(new ModLibrary([LocalCollection], Modpacks((GameRoot, [SteamModpack])), LocalCollectionsComplete: true), Create().Load());
    }

    [Fact]
    public async Task Save_Cancelled_StopsWaitingButStillWrites()
    {
        WriteFile("{");
        var clock = new HeldClock();
        var file = Create(clock);
        file.Load();
        using var cancel = new CancellationTokenSource();

        var first = Task.Run(() => file.SaveLocalCollectionsAsync([], TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var second = file.SaveLocalCollectionsAsync([LocalCollection], cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        clock.Release.Set();
        await first;
        await file.SaveModpacksAsync(Modpacks(), TestContext.Current.CancellationToken);

        Assert.Equal([LocalCollection], Create().Load().LocalCollections);
    }

    [Fact]
    public async Task Flush_WaitsForTheRunningAndTheWaitingWrite()
    {
        WriteFile("{");
        var clock = new HeldClock();
        var file = Create(clock);
        file.Load();
        await file.FlushAsync(TestContext.Current.CancellationToken);

        _ = Task.Run(() => file.SaveLocalCollectionsAsync([], TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        _ = file.SaveLocalCollectionsAsync([LocalCollection], TestContext.Current.CancellationToken);
        var flush = file.FlushAsync(TestContext.Current.CancellationToken);
        Assert.False(flush.IsCompleted);
        clock.Release.Set();
        await flush;

        Assert.Equal([LocalCollection], Create().Load().LocalCollections);
    }

    // Holds the first copy-aside until released.
    private sealed class HeldClock : TimeProvider
    {
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public int Calls => _calls;

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _calls);
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
