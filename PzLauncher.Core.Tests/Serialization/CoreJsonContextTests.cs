using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Serialization;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Tests.Serialization;

public class CoreJsonContextTests
{
    [Fact]
    public void InstallScanResult_RoundTrips_WithCamelCaseAndStringEnums()
    {
        var original = new InstallScanResult(
            [
                new PzInstall(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Server", new SteamAppInfo("123", "unstable")),
                new PzInstall(PzInstallKind.Game, @"D:\Games\PZ", Steam: null),
            ],
            [new Error(ErrorCode.InstallFilesMissing, "msg", ErrorKind.NotFound) { Details = new Dictionary<string, string> { ["path"] = "p" } }]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.InstallScanResult);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.InstallScanResult);

        Assert.Contains("\"code\":\"install.files_missing\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.Installs, copy.Installs);
        var warning = Assert.Single(copy.Warnings);
        Assert.Equal((ErrorCode.InstallFilesMissing, ErrorKind.NotFound), (warning.Code, warning.Kind));
        Assert.Equal("p", warning.Details["path"]);
    }

    [Theory]
    [InlineData(true, """{"kind":"DedicatedServer","rootPath":"/srv/pz","steam":{"buildId":"123","branch":"unstable"},"source":"SteamLibrary"}""")]
    [InlineData(false, """{"kind":"DedicatedServer","rootPath":"/srv/pz","steam":null,"source":"Manual"}""")]
    public void PzInstall_Serialize_WritesSteamInfoAndDerivedSource(bool fromSteam, string expected)
    {
        var install = new PzInstall(PzInstallKind.DedicatedServer, "/srv/pz", fromSteam ? new SteamAppInfo("123", "unstable") : null);

        Assert.Equal(expected, JsonSerializer.Serialize(install, CoreJsonContext.Default.PzInstall));
    }

    [Fact]
    public void PzInstall_SourceInJson_IsIgnoredAndDerivedFromSteam()
    {
        var copy = JsonSerializer.Deserialize(
            """{"kind":"Game","rootPath":"/games/pz","steam":null,"source":"SteamLibrary"}""",
            CoreJsonContext.Default.PzInstall);

        Assert.Equal(PzInstallSource.Manual, copy?.Source);
    }

    [Theory]
    [InlineData("""{"code":"steam.not_installed","message":"msg","kind":"NotFound","details":{}}""")]
    [InlineData("""{"code":"steam.not_installed","message":"msg","kind":"NotFound"}""")]
    public void Error_EmptyOrMissingDetailsInJson_DeserializesAsEmpty(string json)
    {
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Error);

        Assert.NotNull(copy);
        Assert.Empty(copy.Details);
    }

    [Fact]
    public void Error_WithoutDetails_SerializesEmptyObject()
    {
        var json = JsonSerializer.Serialize(new Error(ErrorCode.SteamNotInstalled, "msg", ErrorKind.NotFound), CoreJsonContext.Default.Error);

        Assert.Contains("\"details\":{}", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"code":"steam.not_installed","message":"msg","kind":"NotFound","details":null}""")]
    [InlineData("""{"code":"steam.not_installed","message":null,"kind":"NotFound"}""")]
    [InlineData("""{"code":"steam.not_installed","kind":"NotFound"}""")]
    public void Error_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.Error));

    [Theory]
    [InlineData("""{"kind":"Game","rootPath":null,"steam":null}""")]
    [InlineData("""{"kind":"Game","steam":null}""")]
    [InlineData("""{"kind":"Game","rootPath":"/games/pz","steam":{"buildId":null,"branch":null}}""")]
    public void PzInstall_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.PzInstall));

    [Fact]
    public void JavaScanResult_RoundTrips_WithCamelCaseAndStringEnums()
    {
        var original = new JavaScanResult(
            [
                new JavaRuntime(@"C:\Java\jdk-21", @"C:\Java\jdk-21\bin\javaw.exe", new JavaVersion(21, "21.0.9"), "Eclipse Adoptium", JavaRuntimeSource.Registry, HasGraalCompiler: false, HasInstrumentLibrary: true),
                new JavaRuntime("/opt/jdk", "/opt/jdk/bin/java", Version: null, Vendor: null, JavaRuntimeSource.Manual, HasGraalCompiler: true, HasInstrumentLibrary: false),
            ],
            [new Error(ErrorCode.JavaReleaseUnreadable, "msg", ErrorKind.External) { Details = new Dictionary<string, string> { ["path"] = "p" } }]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.JavaScanResult);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.JavaScanResult);

        Assert.Contains("\"code\":\"java.release_unreadable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"Registry\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.Runtimes, copy.Runtimes);
        Assert.Equal(ErrorCode.JavaReleaseUnreadable, Assert.Single(copy.Warnings).Code);
    }

    [Theory]
    [InlineData("""{"homePath":null,"executablePath":"/x/bin/java","version":null,"vendor":null,"source":"Manual","hasGraalCompiler":false,"hasInstrumentLibrary":true}""")]
    [InlineData("""{"executablePath":"/x/bin/java","version":null,"vendor":null,"source":"Manual","hasGraalCompiler":false,"hasInstrumentLibrary":true}""")]
    [InlineData("""{"homePath":"/x","executablePath":"/x/bin/java","version":{"major":21,"full":null},"vendor":null,"source":"Manual","hasGraalCompiler":false,"hasInstrumentLibrary":true}""")]
    public void JavaRuntime_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.JavaRuntime));

    [Fact]
    public void LaunchGameRequest_RoundTrips()
    {
        var original = new LaunchGameRequest(@"E:\Games\PZ", new GameLaunchOverrides(@"C:\Java\jdk-25", 8192, null, "-Da=\"b c\"", "-debug", GarbageCollector.Shenandoah));

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.LaunchGameRequest);

        Assert.Contains("\"maxMemoryMb\":8192", json, StringComparison.Ordinal);
        Assert.Contains("\"garbageCollector\":\"Shenandoah\"", json, StringComparison.Ordinal);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.LaunchGameRequest));
    }

    [Fact]
    public void GameLaunchOverrides_Empty_RoundTrips() =>
        Assert.Equal(
            GameLaunchOverrides.Empty,
            JsonSerializer.Deserialize(
                JsonSerializer.Serialize(GameLaunchOverrides.Empty, CoreJsonContext.Default.GameLaunchOverrides),
                CoreJsonContext.Default.GameLaunchOverrides));

    [Theory]
    [InlineData("""{"javaHome":null,"maxMemoryMb":null,"minMemoryMb":null,"extraJvmArgs":null,"extraGameArgs":""}""")]
    [InlineData("""{"javaHome":null,"maxMemoryMb":null,"minMemoryMb":null,"extraGameArgs":""}""")]
    public void GameLaunchOverrides_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.GameLaunchOverrides));

    [Fact]
    public void GameLaunchDefaults_RoundTrips()
    {
        var original = new GameLaunchDefaults(
            3072,
            null,
            ["-Xmx3072m", "-XX:+UseZGC"],
            new JavaRuntime("/games/pz/jre64", "/games/pz/jre64/bin/java", new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true),
            32768,
            [new Error(ErrorCode.LaunchConfigMissing, "msg", ErrorKind.NotFound) { Details = new Dictionary<string, string> { ["path"] = "p" } }],
            GarbageCollector.Shenandoah,
            GarbageCollectorSetByViewpoint: true);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.GameLaunchDefaults);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.GameLaunchDefaults);

        Assert.Contains("\"code\":\"launch.config_missing\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal((original.MaxMemoryMb, original.MinMemoryMb, original.BundledJava, original.SystemMemoryMb), (copy.MaxMemoryMb, copy.MinMemoryMb, copy.BundledJava, copy.SystemMemoryMb));
        Assert.Equal((GarbageCollector.Shenandoah, true), (copy.GarbageCollector, copy.GarbageCollectorSetByViewpoint));
        Assert.Equal(original.VmArgs, copy.VmArgs);
        Assert.Equal(ErrorCode.LaunchConfigMissing, Assert.Single(copy.Warnings).Code);
    }

    [Fact]
    public void GameSession_RoundTrips()
    {
        var original = new GameSession("/games/pz", 4242);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.GameSession);

        Assert.Equal("""{"installRoot":"/games/pz","processId":4242}""", json);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.GameSession));
    }

    [Fact]
    public void ZombieBuddyStatus_RoundTrips_WithStringEnums()
    {
        var original = new ZombieBuddyStatus(
            IsSupported: true,
            [
                new ZombieBuddyItem(ZombieBuddyChannel.Stable, @"E:\SteamLibrary\steamapps\workshop\content\108600\3619862853"),
                new ZombieBuddyItem(ZombieBuddyChannel.Beta, "/home/u/.local/share/Steam/steamapps/workshop/content/108600/3812624292"),
            ],
            IsInitialized: false,
            HasLeftovers: true,
            IsAllowAll: true,
            ConfigFolder: "/home/u/.zombie_buddy",
            ZombieBuddyAction.Initialize);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ZombieBuddyStatus);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ZombieBuddyStatus);

        Assert.Contains("\"channel\":\"Stable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"neededAction\":\"Initialize\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.Subscribed, copy.Subscribed);
        Assert.Equal(
            (original.IsSupported, original.IsInitialized, original.HasLeftovers, original.IsAllowAll, original.ConfigFolder, original.NeededAction),
            (copy.IsSupported, copy.IsInitialized, copy.HasLeftovers, copy.IsAllowAll, copy.ConfigFolder, copy.NeededAction));
    }

    [Fact]
    public void ZombieBuddyProgress_RoundTrips_WithStringEnums()
    {
        var original = new ZombieBuddyProgress([ZombieBuddyStep.FindItem, ZombieBuddyStep.CopyAgentJar, ZombieBuddyStep.AddAgentArg], 3, IsComplete: true);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ZombieBuddyProgress);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ZombieBuddyProgress);

        Assert.Equal("""{"steps":["FindItem","CopyAgentJar","AddAgentArg"],"current":3,"isComplete":true}""", json);
        Assert.NotNull(copy);
        Assert.Equal(original.Steps, copy.Steps);
        Assert.Equal((3, true), (copy.Current, copy.IsComplete));
    }

    [Fact]
    public void ZombieBuddyPreload_EveryValue_RoundTripsAsItsName()
    {
        foreach (var value in Enum.GetValues<ZombieBuddyPreload>())
        {
            var json = JsonSerializer.Serialize(value, CoreJsonContext.Default.ZombieBuddyPreload);

            Assert.Equal($"\"{value}\"", json);
            Assert.Equal(value, JsonSerializer.Deserialize(json, CoreJsonContext.Default.ZombieBuddyPreload));
        }
    }

    [Fact]
    public void ModCollectionState_RoundTrips_WithStringEnums()
    {
        var original = new ModCollectionState(
            @"C:\Users\u\Zomboid",
            [new ModCollection("B42·20通用合集", ["ModA", "Missing"], IsActive: true, IsActiveInOrder: false)],
            [
                new InstalledMod("ModA", "Mod A", "2000", InstalledModSource.SteamWorkshop, ["damnlib"], ["B"], ["C"], ["D"]),
                new InstalledMod("Local", "Local", null, InstalledModSource.Local, [], [], [], []),
            ],
            ["ModA"],
            ActiveMatchesCollection: true);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ModCollectionState);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ModCollectionState);

        Assert.Contains("\"source\":\"SteamWorkshop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"workshopId\":null", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal((original.CacheFolder, original.ActiveMatchesCollection), (copy.CacheFolder, copy.ActiveMatchesCollection));
        Assert.Equal(original.ActiveIds, copy.ActiveIds);
        var collection = Assert.Single(copy.Collections);
        Assert.Equal(("B42·20通用合集", true, false), (collection.Name, collection.IsActive, collection.IsActiveInOrder));
        Assert.Equal(["ModA", "Missing"], collection.ModIds);
        Assert.Equal(
            original.InstalledMods.Select(mod => (mod.Id, mod.Name, mod.WorkshopId, mod.Source)),
            copy.InstalledMods.Select(mod => (mod.Id, mod.Name, mod.WorkshopId, mod.Source)));
        var mod = copy.InstalledMods[0];
        Assert.Equal(["damnlib", "B", "C", "D"], mod.Require.Concat(mod.LoadAfter).Concat(mod.LoadBefore).Concat(mod.Incompatible));
    }

    [Theory]
    [InlineData("""{"name":"pack","modIds":null,"isActive":true,"isActiveInOrder":true}""")]
    [InlineData("""{"name":"pack","modIds":[],"isActive":true}""")]
    public void ModCollection_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.ModCollection));

    [Fact]
    public void SaveModCollectionRequest_RoundTrips()
    {
        var original = new SaveModCollectionRequest(@"E:\Games\PZ", @"-cachedir=D:\pz", "old", "new", ["B", "A"]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.SaveModCollectionRequest);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.SaveModCollectionRequest);

        Assert.NotNull(copy);
        Assert.Equal(
            (original.InstallRoot, original.ExtraGameArgs, original.OriginalName, original.Name),
            (copy.InstallRoot, copy.ExtraGameArgs, copy.OriginalName, copy.Name));
        Assert.Equal(["B", "A"], copy.ModIds);
    }

    [Theory]
    [InlineData("""{"installRoot":"/pz","extraGameArgs":"","originalName":null,"name":null,"modIds":[]}""")]
    [InlineData("""{"installRoot":"/pz","extraGameArgs":"","originalName":null,"name":"pack","modIds":null}""")]
    [InlineData("""{"installRoot":"/pz","originalName":null,"name":"pack","modIds":[]}""")]
    public void SaveModCollectionRequest_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.SaveModCollectionRequest));

    [Theory]
    [InlineData("""{"id":null,"name":"A","workshopId":null,"source":"Local","require":[],"loadAfter":[],"loadBefore":[],"incompatible":[]}""")]
    [InlineData("""{"id":"A","name":"A","workshopId":null,"source":"Local","require":null,"loadAfter":[],"loadBefore":[],"incompatible":[]}""")]
    [InlineData("""{"id":"A","name":"A","source":"Local","require":[],"loadAfter":[],"loadBefore":[],"incompatible":[]}""")]
    public void InstalledMod_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.InstalledMod));

    [Theory]
    [InlineData("""{"enabledIds":[],"missingIds":null,"skippedIncompatible":[]}""")]
    [InlineData("""{"enabledIds":[],"missingIds":[],"skippedIncompatible":[{"id":"C","conflictsWith":null}]}""")]
    [InlineData("""{"enabledIds":[],"missingIds":[]}""")]
    public void ApplyModCollectionResult_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.ApplyModCollectionResult));

    [Fact]
    public void ApplyModCollectionResult_RoundTrips()
    {
        var original = new ApplyModCollectionResult(["A", "B"], ["Missing"], [new ModConflict("C", "A")]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ApplyModCollectionResult);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ApplyModCollectionResult);

        Assert.Contains("""
            "skippedIncompatible":[{"id":"C","conflictsWith":"A"}]
            """, json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.EnabledIds, copy.EnabledIds);
        Assert.Equal(original.MissingIds, copy.MissingIds);
        Assert.Equal(original.SkippedIncompatible, copy.SkippedIncompatible);
    }

    [Fact]
    public void SharedModCollection_RoundTrips()
    {
        var original = new SharedModCollection("pack", ["A"]);

        var copy = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, CoreJsonContext.Default.SharedModCollection), CoreJsonContext.Default.SharedModCollection);

        Assert.NotNull(copy);
        Assert.Equal("pack", copy.Name);
        Assert.Equal(["A"], copy.ModIds);
    }
    [Fact]
    public void UpdateCheck_RoundTrips_WithVersionStringsAndStringEnums()
    {
        var original = new UpdateCheck(
            new SemVersion(0, 2, 0, ""),
            new ReleaseInfo(
                new SemVersion(0, 3, 0, ""),
                UpdateSource.Gitee,
                "PzLauncher-0.3.0-win-x64.zip",
                new Uri("https://gitee.com/dontcampy/pzl/releases/download/v0.3.0/PzLauncher-0.3.0-win-x64.zip"),
                Size: null,
                Sha256: null,
                new Uri("https://gitee.com/dontcampy/pzl/releases/tag/v0.3.0")));

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.UpdateCheck);

        Assert.Contains("\"current\":\"0.2.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"Gitee\"", json, StringComparison.Ordinal);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.UpdateCheck));
    }

    [Fact]
    public void UpdateCheck_UpToDate_RoundTrips() =>
        Assert.Equal(
            new UpdateCheck(new SemVersion(1, 0, 0, "rc.1"), null),
            JsonSerializer.Deserialize(
                JsonSerializer.Serialize(new UpdateCheck(new SemVersion(1, 0, 0, "rc.1"), null), CoreJsonContext.Default.UpdateCheck),
                CoreJsonContext.Default.UpdateCheck));

    [Theory]
    [InlineData("""{"current":null,"available":null}""")]
    [InlineData("""{"available":null}""")]
    [InlineData("""{"current":"0.2.0","available":{"version":"0.3.0","source":"GitHub","packageName":null,"packageUrl":"https://x.test/p","size":null,"sha256":null,"releasePageUrl":"https://x.test/"}}""")]
    public void UpdateCheck_NonNullMemberNullOrMissingInJson_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.UpdateCheck));

    [Fact]
    public void StagedUpdate_RoundTrips()
    {
        var original = new StagedUpdate(new SemVersion(0, 3, 0, ""), "/opt/pzlauncher/.update/staged/0.3.0", new Uri("https://github.com/pz-launcher/launcher/releases/tag/v0.3.0"));

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.StagedUpdate);

        Assert.Contains("\"version\":\"0.3.0\"", json, StringComparison.Ordinal);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.StagedUpdate));
    }

    [Fact]
    public void DownloadProgress_RoundTrips()
    {
        var original = new DownloadProgress(1024, null);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.DownloadProgress);

        Assert.Equal("""{"received":1024,"total":null}""", json);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.DownloadProgress));
    }

    [Fact]
    public void UpdateSource_EveryValue_RoundTripsAsItsName()
    {
        foreach (var value in Enum.GetValues<UpdateSource>())
        {
            var json = JsonSerializer.Serialize(value, CoreJsonContext.Default.UpdateSource);

            Assert.Equal($"\"{value}\"", json);
            Assert.Equal(value, JsonSerializer.Deserialize(json, CoreJsonContext.Default.UpdateSource));
        }
    }
}
