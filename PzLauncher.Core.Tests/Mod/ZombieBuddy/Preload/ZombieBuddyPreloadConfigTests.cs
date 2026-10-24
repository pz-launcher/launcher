using System.Text.Json.Nodes;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy.Preload;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy.Preload;

public sealed class ZombieBuddyPreloadConfigTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _configFolder;
    private readonly ZombieBuddyPreloadEntry _entry;

    public ZombieBuddyPreloadConfigTests()
    {
        _configFolder = Path.Combine(_temp.Path, "zb", ".zombie_buddy");
        var version = ZabricBuilder.Create(Path.Combine(_temp.Path, "Workshop", "Zabric"));
        _entry = new ZombieBuddyPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version));
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ConfigPath => Path.Combine(_configFolder, "config.json");

    private Task<Result<ZombieBuddyPreload>> EnsureAsync(StringComparison pathComparison = StringComparison.Ordinal) =>
        ZombieBuddyPreloadConfig.EnsureAsync(_configFolder, "Zabric", _entry, first: true, pathComparison, Ct);

    private Task<Result<ZombieBuddyPreload>> EnsureOtherModAsync(ZombieBuddyPreloadEntry entry) =>
        ZombieBuddyPreloadConfig.EnsureAsync(_configFolder, "OtherMod", entry, first: false, StringComparison.Ordinal, Ct);

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

    private ZombieBuddyPreloadEntry OtherCopy(string classes)
    {
        var version = ZabricBuilder.Create(Path.Combine(_temp.Path, "Server", "Zabric"), classes: classes);
        return new ZombieBuddyPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version));
    }

    private void AssertZabricFirst(JsonObject expectedEntry)
    {
        var preload = ReadConfig()["preload_mods"]!.AsObject();
        Assert.Equal("Zabric", preload.First().Key);
        Assert.True(JsonNode.DeepEquals(expectedEntry, preload["Zabric"]), preload["Zabric"]?.ToJsonString());
    }

    [Fact]
    public async Task EnsureAsync_NoFileOrFolder_CreatesTheConfigWithTheEntry()
    {
        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    [InlineData("""{"preload_mods": null}""")]
    public async Task EnsureAsync_BlankFileOrNullPreloadMods_WritesTheEntry(string json)
    {
        WriteConfig(json);

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Fact]
    public async Task EnsureAsync_SamePathsInAnotherCase_IsCurrentWithoutWriting()
    {
        var json = ConfigWith(("Zabric", _entry.InfPath.ToUpperInvariant(), _entry.JarPath.ToUpperInvariant()));
        WriteConfig(json);

        Assert.Equal(ZombieBuddyPreload.Current, ResultAssert.Ok(await EnsureAsync(StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task EnsureAsync_ByteIdenticalCopyElsewhere_IsCurrentWithoutWriting()
    {
        var other = OtherCopy("v1");
        var json = ConfigWith(("Zabric", other.InfPath, other.JarPath), ("OtherMod", "a", "b"));
        WriteConfig(json);

        Assert.Equal(ZombieBuddyPreload.Current, ResultAssert.Ok(await EnsureAsync()));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureAsync_StoredJarDiffersOrIsMissing_ReplacesTheEntry(bool missing)
    {
        var other = OtherCopy("v2");
        if (missing)
        {
            File.Delete(other.JarPath);
        }

        WriteConfig(ConfigWith(("Zabric", other.InfPath, other.JarPath)));

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(_entry.InfPath, _entry.JarPath));
    }

    [Fact]
    public async Task EnsureAsync_CurrentButNotFirst_MovesTheStoredEntryFirst()
    {
        var other = OtherCopy("v1");
        WriteConfig(ConfigWith(("OtherMod", "a", "b"), ("Zabric", other.InfPath, other.JarPath)));

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureAsync()));

        AssertZabricFirst(Entry(other.InfPath, other.JarPath));
        Assert.Equal(["Zabric", "OtherMod"], ReadConfig()["preload_mods"]!.AsObject().Select(mod => mod.Key));
    }

    [Fact]
    public async Task EnsureAsync_NotFirstAndCurrent_LeavesItsPlaceWithoutWriting()
    {
        var other = OtherCopy("v1");
        var json = ConfigWith(("Zabric", "a", "b"), ("OtherMod", other.InfPath, other.JarPath));
        WriteConfig(json);

        Assert.Equal(ZombieBuddyPreload.Current, ResultAssert.Ok(await EnsureOtherModAsync(_entry)));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task EnsureAsync_NotFirstAndStale_ReplacesTheEntryInPlace()
    {
        var stale = OtherCopy("v2");
        WriteConfig(ConfigWith(("Zabric", "a", "b"), ("OtherMod", stale.InfPath, stale.JarPath), ("Last", "c", "d")));

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureOtherModAsync(_entry)));

        var preload = ReadConfig()["preload_mods"]!.AsObject();
        Assert.Equal(["Zabric", "OtherMod", "Last"], preload.Select(mod => mod.Key));
        Assert.True(JsonNode.DeepEquals(Entry(_entry.InfPath, _entry.JarPath), preload["OtherMod"]));
    }

    [Fact]
    public async Task EnsureAsync_NotFirstAndAbsent_AddsTheEntryLast()
    {
        WriteConfig(ConfigWith(("Zabric", "a", "b")));

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureOtherModAsync(_entry)));

        Assert.Equal(["Zabric", "OtherMod"], ReadConfig()["preload_mods"]!.AsObject().Select(mod => mod.Key));
    }

    [Fact]
    public async Task EnsureAsync_RealConfig_KeepsEveryOtherFieldAndEntry()
    {
        var original = JsonNode.Parse(Fixtures.Read("ZombieBuddy", "config.json"))!.AsObject();
        WriteConfig(Fixtures.Read("ZombieBuddy", "config.json"));

        Assert.Equal(ZombieBuddyPreload.Written, ResultAssert.Ok(await EnsureAsync()));

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
    public async Task EnsureAsync_InvalidConfig_ReturnsPreloadConfigInvalidAndLeavesTheFile(string json)
    {
        WriteConfig(json);

        var error = ResultAssert.Failed(await EnsureAsync(), ErrorCode.ZombieBuddyPreloadConfigInvalid);

        Assert.Equal((ConfigPath, "Zabric"), (error.Details["path"], error.Details["modId"]));
        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task EnsureAsync_ConfigLocked_ReturnsPreloadConfigIoFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to read a file opened without sharing.");
        WriteConfig("{}");

        using var locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var error = ResultAssert.Failed(await EnsureAsync(), ErrorCode.ZombieBuddyPreloadConfigIoFailed);

        Assert.Equal((ConfigPath, "Zabric"), (error.Details["path"], error.Details["modId"]));
    }

    private Task<Result<ZombieBuddyPreload>> RemoveOtherModAsync() => ZombieBuddyPreloadConfig.RemoveAsync(_configFolder, "OtherMod", Ct);

    [Fact]
    public async Task RemoveAsync_NoFileOrFolder_IsNotApplicableAndCreatesNothing()
    {
        Assert.Equal(ZombieBuddyPreload.NotApplicable, ResultAssert.Ok(await RemoveOtherModAsync()));

        Assert.False(Directory.Exists(_configFolder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"trusted_authors": []}""")]
    [InlineData("""{"preload_mods": null}""")]
    [InlineData("""{"preload_mods": {"Zabric": {"infPath": "a", "jarPath": "b"}}}""")]
    public async Task RemoveAsync_NoEntry_IsNotApplicableWithoutWriting(string json)
    {
        WriteConfig(json);

        Assert.Equal(ZombieBuddyPreload.NotApplicable, ResultAssert.Ok(await RemoveOtherModAsync()));

        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task RemoveAsync_RealConfig_DropsOnlyTheEntry()
    {
        var original = JsonNode.Parse(Fixtures.Read("ZombieBuddy", "config.json"))!.AsObject();
        WriteConfig(Fixtures.Read("ZombieBuddy", "config.json"));

        Assert.Equal(ZombieBuddyPreload.Removed, ResultAssert.Ok(await RemoveOtherModAsync()));

        var written = ReadConfig();
        Assert.Equal(original.Select(field => field.Key), written.Select(field => field.Key));
        foreach (var (key, value) in original.Where(field => field.Key != "preload_mods"))
        {
            Assert.True(JsonNode.DeepEquals(value, written[key]), key);
        }

        var preload = written["preload_mods"]!.AsObject();
        Assert.Equal(["Zabric"], preload.Select(mod => mod.Key));
        Assert.True(JsonNode.DeepEquals(original["preload_mods"]!["Zabric"], preload["Zabric"]));
    }

    [Fact]
    public async Task RemoveAsync_EntryBetweenOthers_KeepsTheirOrder()
    {
        WriteConfig(ConfigWith(("Zabric", "a", "b"), ("OtherMod", "c", "d"), ("Last", "e", "f")));

        Assert.Equal(ZombieBuddyPreload.Removed, ResultAssert.Ok(await RemoveOtherModAsync()));

        Assert.Equal(["Zabric", "Last"], ReadConfig()["preload_mods"]!.AsObject().Select(mod => mod.Key));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"preload_mods": 1}""")]
    public async Task RemoveAsync_InvalidConfig_ReturnsPreloadConfigInvalidAndLeavesTheFile(string json)
    {
        WriteConfig(json);

        var error = ResultAssert.Failed(await RemoveOtherModAsync(), ErrorCode.ZombieBuddyPreloadConfigInvalid);

        Assert.Equal((ConfigPath, "OtherMod"), (error.Details["path"], error.Details["modId"]));
        Assert.Equal(json, await File.ReadAllTextAsync(ConfigPath, Ct));
    }

    [Fact]
    public async Task RemoveAsync_ConfigLocked_ReturnsPreloadConfigIoFailed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to read a file opened without sharing.");
        WriteConfig(ConfigWith(("OtherMod", "a", "b")));

        using var locked = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var error = ResultAssert.Failed(await RemoveOtherModAsync(), ErrorCode.ZombieBuddyPreloadConfigIoFailed);

        Assert.Equal((ConfigPath, "OtherMod"), (error.Details["path"], error.Details["modId"]));
    }
}
