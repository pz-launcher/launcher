using System.Text.Json.Nodes;
using PzLauncher.Core.Common;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Tests.ZombieBuddy;

public sealed class ZombieBuddyConfigEditorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _configFolder;
    private readonly ZabricPreloadEntry _entry;

    public ZombieBuddyConfigEditorTests()
    {
        _configFolder = Path.Combine(_temp.Path, "zb", ".zombie_buddy");
        var version = ZabricBuilder.Create(Path.Combine(_temp.Path, "Workshop", "Zabric"));
        _entry = new ZabricPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version));
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ConfigPath => Path.Combine(_configFolder, "config.json");

    private Task<Result<ZabricPreload>> EnsureAsync(StringComparison pathComparison = StringComparison.Ordinal) =>
        ZombieBuddyConfigEditor.EnsureZabricAsync(_configFolder, _entry, pathComparison, Ct);

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_configFolder);
        File.WriteAllText(ConfigPath, json);
    }

    private JsonObject ReadConfig() => JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();

    private static JsonObject Entry(string inf, string jar) => new() { ["infPath"] = inf, ["jarPath"] = jar };

    private static string ConfigWith(params (string Id, string Inf, string Jar)[] mods) =>
        new JsonObject { ["preload_mods"] = new JsonObject(mods.Select(mod => KeyValuePair.Create(mod.Id, (JsonNode?)Entry(mod.Inf, mod.Jar)))) }
            .ToJsonString();

    private ZabricPreloadEntry OtherCopy(string classes)
    {
        var version = ZabricBuilder.Create(Path.Combine(_temp.Path, "Server", "Zabric"), classes: classes);
        return new ZabricPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version));
    }

    private void AssertZabricFirst(JsonObject expectedEntry)
    {
        var preload = ReadConfig()["preload_mods"]!.AsObject();
        Assert.Equal("Zabric", preload.First().Key);
        Assert.True(JsonNode.DeepEquals(expectedEntry, preload["Zabric"]), preload["Zabric"]?.ToJsonString());
    }

    [Fact]
    public async Task EnsureZabricAsync_NoFileOrFolder_CreatesTheConfigWithTheEntry()
    {
        Assert.Equal(ZabricPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    [InlineData("""{"preload_mods": null}""")]
    public async Task EnsureZabricAsync_BlankFileOrNullPreloadMods_WritesTheEntry(string json)
    {
        WriteConfig(json);

        Assert.Equal(ZabricPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Fact]
    public async Task EnsureZabricAsync_SamePathsInAnotherCase_IsCurrentWithoutWriting()
    {
        var json = ConfigWith(("Zabric", _entry.InfPath.ToUpperInvariant(), _entry.JarPath.ToUpperInvariant()));
        WriteConfig(json);

        Assert.Equal(ZabricPreload.Current, ResultAssert.Ok(await EnsureAsync(StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task EnsureZabricAsync_ByteIdenticalCopyElsewhere_IsCurrentWithoutWriting()
    {
        var other = OtherCopy("v1");
        var json = ConfigWith(("Zabric", other.InfPath, other.JarPath), ("OtherMod", "a", "b"));
        WriteConfig(json);

        Assert.Equal(ZabricPreload.Current, ResultAssert.Ok(await EnsureAsync()));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureZabricAsync_StoredJarDiffersOrIsMissing_ReplacesTheEntry(bool missing)
    {
        var other = OtherCopy("v2");
        if (missing)
        {
            File.Delete(other.JarPath);
        }

        WriteConfig(ConfigWith(("Zabric", other.InfPath, other.JarPath)));

        Assert.Equal(ZabricPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Fact]
    public async Task EnsureZabricAsync_CurrentButNotFirst_MovesTheStoredEntryFirst()
    {
        var other = OtherCopy("v1");
        WriteConfig(ConfigWith(("OtherMod", "a", "b"), ("Zabric", other.InfPath, other.JarPath)));

        Assert.Equal(ZabricPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(other.InfPath, other.JarPath));
        Assert.Equal(["Zabric", "OtherMod"], ReadConfig()["preload_mods"]!.AsObject().Select(mod => mod.Key));
    }

    [Fact]
    public async Task EnsureZabricAsync_RealConfig_KeepsEveryOtherFieldAndEntry()
    {
        var original = JsonNode.Parse(Fixtures.Read("ZombieBuddy", "config.json"))!.AsObject();
        WriteConfig(Fixtures.Read("ZombieBuddy", "config.json"));

        Assert.Equal(ZabricPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        var written = ReadConfig();
        Assert.Equal(original.Select(field => field.Key), written.Select(field => field.Key));
        foreach (var (key, value) in original.Where(field => field.Key != "preload_mods"))
        {
            Assert.True(JsonNode.DeepEquals(value, written[key]), key);
        }

        var preload = written["preload_mods"]!.AsObject();
        Assert.Equal(["Zabric", "OtherMod"], preload.Select(mod => mod.Key));
        Assert.True(JsonNode.DeepEquals(original["preload_mods"]!["OtherMod"], preload["OtherMod"]));
        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"preload_mods": 1}""")]
    [InlineData("""{"preload_mods": []}""")]
    [InlineData("""{"trusted_authors": [], "trusted_authors": [1]}""")]
    public async Task EnsureZabricAsync_InvalidConfig_ReturnsPreloadConfigInvalidAndLeavesTheFile(string json)
    {
        WriteConfig(json);

        var error = ResultAssert.Failed(await EnsureAsync(), ErrorCode.ZombieBuddyPreloadConfigInvalid);

        Assert.Equal(ConfigPath, error.Details["path"]);
        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task EnsureZabricAsync_ConfigLocked_ReturnsPreloadConfigIoFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to read a file opened without sharing.");
        WriteConfig("{}");

        using var locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var error = ResultAssert.Failed(await EnsureAsync(), ErrorCode.ZombieBuddyPreloadConfigIoFailed);

        Assert.Equal(ConfigPath, error.Details["path"]);
    }
}
