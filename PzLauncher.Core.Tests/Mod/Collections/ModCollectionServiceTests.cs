using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.Collections;

public sealed class ModCollectionServiceTests : IDisposable
{
    private const string Favourites = "!fav!:FavA;FavB;";

    private const string ActiveAB =
        "VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = A,\r\n    mod = B,\r\n}\r\n\r\nmaps\r\n{\r\n    map = Muldraugh, KY,\r\n}\r\n";

    private readonly TempDirectory _temp = new();
    private readonly FakePlatformEnvironment _platform;
    private readonly ModCollectionService _service;
    private readonly ModBuilder _mods;
    private readonly string _root;

    public ModCollectionServiceTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        var library = steam.CreateLibrary("Library");
        var home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(library));
        _platform.HomeDirectory = home;
        _service = new ModCollectionService(
            ZombieBuddyServiceTests.CreateWorkshopLibraries(_platform), _platform, NullLogger<ModCollectionService>.Instance);
        _root = new GameInstallBuilder(Path.Combine(library, "steamapps", "common"), PlatformKind.Windows)
            .CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJson, withJre: false);
        _mods = new ModBuilder(Path.Combine(home, "Zomboid"), library);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SettingsPath => Path.Combine(_mods.Cache, "Lua", "pz_modlist_settings.cfg");

    private string ActivePath => Path.Combine(_mods.Cache, "mods", "default.txt");

    private void WriteSettings(params string[] lines) => WriteFile(SettingsPath, string.Concat(lines.Select(line => line + "\n")));

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Settings() => File.ReadAllText(SettingsPath);

    private async Task<ModCollectionState> GetAsync() => ResultAssert.Ok(await _service.GetAsync(_root, "", Ct));

    private async Task<IReadOnlyList<string>> IdsOfAsync(string name) =>
        Assert.Single((await GetAsync()).Collections, collection => collection.Name == name).ModIds;

    private Task<Result> SaveAsync(string name, string[] ids, string? originalName = null) =>
        _service.SaveAsync(new SaveModCollectionRequest(_root, "", originalName, name, ids), Ct);

    [Fact]
    public async Task GetAsync_CollectionsModsAndEnabledList_AreListedWithTheInUseMarker()
    {
        _mods.Steam("2000", "A");
        _mods.Local("B", ModBuilder.ModInfo("B", require: "A"));
        _mods.Local("C");
        WriteSettings(Favourites, "pack:B;Missing;A;", "other:C;");
        WriteFile(ActivePath, ActiveAB);

        var state = await GetAsync();

        Assert.Equal(_mods.Cache, state.CacheFolder);
        Assert.Equal(["A", "B", "C"], state.InstalledMods.Select(mod => mod.Id));
        Assert.Equal(["A", "B"], state.ActiveIds);
        Assert.Equal([("pack", true), ("other", false)], state.Collections.Select(c => (c.Name, c.IsActive)));
        Assert.Equal(["B", "Missing", "A"], state.Collections[0].ModIds);
        Assert.True(state.ActiveMatchesCollection);
    }

    [Fact]
    public async Task GetAsync_EnabledListMatchingNoCollection_IsReported()
    {
        _mods.Local("A");
        _mods.Local("B");
        WriteSettings("only A:A;");
        WriteFile(ActivePath, ActiveAB);

        Assert.False((await GetAsync()).ActiveMatchesCollection);
    }

    [Fact]
    public async Task GetAsync_NothingEnabled_CollectionsEnablingNothingAreNotInUse()
    {
        _mods.Local("A");
        WriteSettings("empty:", "gone:Missing;", "pack:A;");
        WriteFile(ActivePath, "VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = Missing,\r\n}\r\n");

        var state = await GetAsync();

        Assert.Equal([("empty", false), ("gone", false), ("pack", false)], state.Collections.Select(c => (c.Name, c.IsActive)));
        Assert.True(state.ActiveMatchesCollection);
    }

    [Fact]
    public async Task GetAsync_NoFiles_IsEmptyAndMatches()
    {
        var state = await GetAsync();

        Assert.Empty(state.Collections);
        Assert.Empty(state.ActiveIds);
        Assert.True(state.ActiveMatchesCollection);
    }

    [Fact]
    public async Task GetAsync_CacheDirArg_ReadsThatCacheFolder()
    {
        var other = _temp.CreateDirectory("other cache");
        WriteFile(Path.Combine(other, "Lua", "pz_modlist_settings.cfg"), "there:A;\n");

        var state = ResultAssert.Ok(await _service.GetAsync(_root, $"-cachedir=\"{other}\"", Ct));

        Assert.Equal("there", Assert.Single(state.Collections).Name);
    }

    [Fact]
    public async Task GetAsync_NoHomeAndNoCacheDir_IsCacheFolderUnresolved()
    {
        _platform.HomeDirectory = "";

        ResultAssert.Failed(await _service.GetAsync(_root, "", Ct), ErrorCode.ModCollectionCacheFolderUnresolved);
    }

    [Fact]
    public async Task SaveAsync_NewCollection_IsAppendedInTheGamesFormat()
    {
        WriteSettings(Favourites, "pack:A;");

        ResultAssert.Ok(await SaveAsync("new", ["Z", "Y", "Z"]));

        Assert.Equal($"{Favourites}\npack:A;\nnew:Z;Y;\n", Settings());
    }

    [Fact]
    public async Task SaveAsync_Reordered_TheLauncherOrderSurvivesAnInGameRewrite()
    {
        WriteSettings(Favourites, "pack:A;B;C;");
        ResultAssert.Ok(await SaveAsync("pack", ["C", "A", "B"]));

        Assert.Equal($"{Favourites}\npack:C;A;B;\n", Settings());

        // The game rewrites every line in hash order on save; "New" was added in-game.
        WriteSettings(Favourites, "pack:B;New;A;C;");

        Assert.Equal(["C", "A", "B", "New"], await IdsOfAsync("pack"));
    }

    [Fact]
    public async Task SaveAsync_Rename_KeepsThePositionAndMovesTheRememberedOrder()
    {
        WriteSettings(Favourites, "pack:A;B;", "keep:K;");
        ResultAssert.Ok(await SaveAsync("pack", ["B", "A"]));

        ResultAssert.Ok(await SaveAsync("renamed", ["B", "A"], originalName: "pack"));
        WriteSettings(Favourites, "renamed:A;B;", "keep:K;", "pack:A;B;");

        Assert.Equal(["B", "A"], await IdsOfAsync("renamed"));
        Assert.Equal(["A", "B"], await IdsOfAsync("pack"));
    }

    [Fact]
    public async Task SaveAsync_RenameOverAnotherCollection_OverwritesIt()
    {
        WriteSettings("a:A;", "b:B;");

        ResultAssert.Ok(await SaveAsync("b", ["A"], originalName: "a"));

        Assert.Equal("b:A;\n", Settings());
    }

    [Fact]
    public async Task SaveAsync_FileEditedAfterGet_KeepsTheOtherEdits()
    {
        WriteSettings(Favourites, "pack:A;", "other:O;");
        await GetAsync();
        WriteSettings(Favourites, "pack:A;", "other:O;Edited;", "ingame:I;");

        ResultAssert.Ok(await SaveAsync("pack", ["A", "B"]));

        Assert.Equal($"{Favourites}\npack:A;B;\nother:O;Edited;\ningame:I;\n", Settings());
    }

    [Theory]
    [InlineData("a:b", "A", ErrorCode.ModCollectionNameInvalid)]
    [InlineData("pack", "A;B", ErrorCode.ModCollectionModIdInvalid)]
    [InlineData("pack", "", ErrorCode.ModCollectionModIdInvalid)]
    public async Task SaveAsync_InvalidInput_FailsWithoutWriting(string name, string id, ErrorCode code)
    {
        ResultAssert.Failed(await SaveAsync(name, [id]), code);

        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task SaveAsync_RenameOfAMissingCollection_IsNotFound()
    {
        WriteSettings("pack:A;");

        ResultAssert.Failed(await SaveAsync("new", ["A"], originalName: "gone"), ErrorCode.ModCollectionNotFound);
        Assert.Equal("pack:A;\n", Settings());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheLineAndItsRememberedOrder()
    {
        WriteSettings(Favourites, "pack:A;B;", "keep:K;");
        ResultAssert.Ok(await SaveAsync("pack", ["B", "A"]));

        ResultAssert.Ok(await _service.DeleteAsync(_root, "", "pack", Ct));
        WriteSettings(Favourites, "keep:K;", "pack:A;B;");

        Assert.Equal(["A", "B"], await IdsOfAsync("pack"));
    }

    [Fact]
    public async Task DeleteAsync_MissingCollection_IsNotFound()
    {
        WriteSettings("pack:A;");

        ResultAssert.Failed(await _service.DeleteAsync(_root, "", "gone", Ct), ErrorCode.ModCollectionNotFound);
    }

    [Fact]
    public async Task ApplyAsync_Collection_WritesTheResolvedListAndKeepsMapsAndBackups()
    {
        _mods.Local("A");
        _mods.Local("B", ModBuilder.ModInfo("B", require: @"\A"));
        _mods.Local("C", ModBuilder.ModInfo("C", incompatible: "A"));
        WriteSettings("pack:B;Missing;C;");
        WriteFile(ActivePath, ActiveAB.Replace("mod = A,", "mod = Old,", StringComparison.Ordinal));
        var original = await File.ReadAllTextAsync(ActivePath, Ct);

        var result = ResultAssert.Ok(await _service.ApplyAsync(_root, "", "pack", Ct));

        Assert.Equal(["A", "B"], result.EnabledIds);
        Assert.Equal(["Missing"], result.MissingIds);
        Assert.Equal([new ModConflict("C", "A")], result.SkippedIncompatible);
        Assert.Equal(ActiveAB, await File.ReadAllTextAsync(ActivePath, Ct));
        Assert.Equal(original, await File.ReadAllTextAsync(ActivePath + ".pzlauncher.bak", Ct));
        Assert.Equal(original, await File.ReadAllTextAsync(ActivePath + ".pzlauncher.orig", Ct));
        Assert.True((await GetAsync()).Collections[0].IsActive);
    }

    [Fact]
    public async Task ApplyAsync_SecondApply_LeavesTheOriginalCopyUnchanged()
    {
        _mods.Local("A");
        _mods.Local("B");
        WriteSettings("a:A;", "b:B;");
        WriteFile(ActivePath, ActiveAB);

        ResultAssert.Ok(await _service.ApplyAsync(_root, "", "a", Ct));
        ResultAssert.Ok(await _service.ApplyAsync(_root, "", "b", Ct));

        Assert.Equal(ActiveAB, await File.ReadAllTextAsync(ActivePath + ".pzlauncher.orig", Ct));
        Assert.Equal(["B"], (await GetAsync()).ActiveIds);
    }

    [Fact]
    public async Task ApplyAsync_RememberedOrder_IsTheLoadOrder()
    {
        _mods.Local("A");
        _mods.Local("B");
        WriteSettings("pack:A;B;");
        ResultAssert.Ok(await SaveAsync("pack", ["B", "A"]));
        WriteSettings("pack:A;B;");

        Assert.Equal(["B", "A"], ResultAssert.Ok(await _service.ApplyAsync(_root, "", "pack", Ct)).EnabledIds);
    }

    [Fact]
    public async Task ApplyAsync_NoDefaultTxt_CreatesItWithTheOsNewline()
    {
        _mods.Local("A");
        WriteSettings("pack:A;");

        ResultAssert.Ok(await _service.ApplyAsync(_root, "", "pack", Ct));

        Assert.Equal("VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = A,\r\n}\r\n\r\nmaps\r\n{\r\n}\r\n", await File.ReadAllTextAsync(ActivePath, Ct));
    }

    [Fact]
    public async Task ApplyAsync_MissingCollection_IsNotFoundAndWritesNothing()
    {
        WriteSettings("pack:A;");

        ResultAssert.Failed(await _service.ApplyAsync(_root, "", "gone", Ct), ErrorCode.ModCollectionNotFound);
        Assert.False(File.Exists(ActivePath));
    }
}
