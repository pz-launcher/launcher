using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Net;
using PzLauncher.Core.Serialization;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam;
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
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorList_RoundTrips(bool withErrors)
    {
        IReadOnlyList<Error> original = withErrors
            ? [new Error(ErrorCode.ZombieBuddyPreloadConfigInvalid, "msg", ErrorKind.Validation) { Details = new Dictionary<string, string> { ["path"] = "p" } },
                new Error(ErrorCode.ZombieBuddyCopyFailed, "copy", ErrorKind.External)]
            : [];

        var copy = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, CoreJsonContext.Default.IReadOnlyListError), CoreJsonContext.Default.IReadOnlyListError);

        Assert.NotNull(copy);
        Assert.Equal(original.Select(error => (error.Code, error.Message, error.Kind)), copy.Select(error => (error.Code, error.Message, error.Kind)));
        Assert.Equal(original.Select(error => error.Details.ToArray()), copy.Select(error => error.Details.ToArray()));
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
                new InstalledMod("ModA", "Mod A", 18446744073709551615, InstalledModSource.SteamWorkshop, ["damnlib"], ["B"], ["C"], ["D"]),
                new InstalledMod("Local", "Local", null, InstalledModSource.Local, [], [], [], []),
            ],
            ["ModA"],
            ActiveMatchesCollection: true);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ModCollectionState);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ModCollectionState);

        Assert.Contains("\"source\":\"SteamWorkshop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"workshopId\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"workshopId\":18446744073709551615", json, StringComparison.Ordinal);
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

    [Fact]
    public void ProxySettings_Manual_RoundTrips()
    {
        var original = new ProxySettings(ProxyMode.Manual, new ManualProxy("127.0.0.1", 7890, "user", "secret"));

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.ProxySettings);

        Assert.Equal("""{"mode":"Manual","manual":{"host":"127.0.0.1","port":7890,"username":"user","password":"secret"}}""", json);
        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.ProxySettings));
    }

    [Fact]
    public void ProxySettings_Default_RoundTrips()
    {
        var json = JsonSerializer.Serialize(ProxySettings.Default, CoreJsonContext.Default.ProxySettings);

        Assert.Equal("""{"mode":"System","manual":null}""", json);
        Assert.Equal(ProxySettings.Default, JsonSerializer.Deserialize(json, CoreJsonContext.Default.ProxySettings));
    }

    [Fact]
    public void ConnectionTestResults_RoundTrip()
    {
        IReadOnlyList<ConnectionTestResult> original =
        [
            new("GitHub", new Uri("https://api.github.com/"), TimeSpan.FromMilliseconds(123), null),
            new("Gitee", new Uri("https://gitee.com/"), TimeSpan.FromSeconds(15), new Error(ErrorCode.NetworkTimeout, "msg", ErrorKind.External)),
        ];

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.IReadOnlyListConnectionTestResult);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.IReadOnlyListConnectionTestResult);

        Assert.Contains("\"code\":\"network.timeout\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.Select(r => (r.Name, r.Url, r.Elapsed, r.Error?.Code)), copy.Select(r => (r.Name, r.Url, r.Elapsed, r.Error?.Code)));
    }

    [Fact]
    public void SteamLaunchHookApplied_RoundTrips_WithStringEnums()
    {
        var original = new SteamLaunchHookApplied(
            new SteamLaunchHookStatus(new SteamAccount(629668875, "Example Player"), SteamLaunchHookState.OtherLauncher, @"D:\Old\PzLauncher.exe", IsSteamRunning: true),
            [new Error(ErrorCode.SteamStartFailed, "msg", ErrorKind.External)]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.SteamLaunchHookApplied);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.SteamLaunchHookApplied);

        Assert.Contains("\"state\":\"OtherLauncher\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"steam.start_failed\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.Status, copy.Status);
        Assert.Equal(ErrorCode.SteamStartFailed, Assert.Single(copy.Warnings).Code);
    }

    [Fact]
    public void WorkshopSubscriptions_RoundTrips_WithStringEnumsAndLargeManifests()
    {
        var latest = new WorkshopItemDetails(3811390574, "Example Mod", "987951682342026743", DateTimeOffset.FromUnixTimeSeconds(1792336800), 253378238);
        var original = new WorkshopSubscriptions(
            [
                new WorkshopItem(
                    3811390574,
                    DateTimeOffset.FromUnixTimeSeconds(1792336673),
                    DisabledLocally: false,
                    new WorkshopInstall(@"E:\SteamLibrary\steamapps\workshop\content\108600\3811390574", "14459181659745603925", DateTimeOffset.FromUnixTimeSeconds(1792336500), 253378238),
                    latest,
                    WorkshopItemState.NeedsUpdate),
                new WorkshopItem(2409333430, DateTimeOffset.FromUnixTimeSeconds(1), DisabledLocally: true, null, null, WorkshopItemState.DisabledLocally),
            ],
            [new Error(ErrorCode.WorkshopManifestUnreadable, "msg", ErrorKind.External) { Details = new Dictionary<string, string> { ["path"] = "p", ["reason"] = "r" } }]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.WorkshopSubscriptions);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.WorkshopSubscriptions);

        Assert.Contains("\"state\":\"NeedsUpdate\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manifest\":\"14459181659745603925\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"workshop.manifest_unreadable\"", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(2, copy.Items.Count);
        var item = copy.Items[0];
        Assert.Equal(
            (original.Items[0].Id, original.Items[0].Subscribed, original.Items[0].Installed, original.Items[0].Latest, original.Items[0].State),
            (item.Id, item.Subscribed, item.Installed, item.Latest, item.State));
        Assert.Equal((true, (WorkshopInstall?)null, (WorkshopItemDetails?)null), (copy.Items[1].DisabledLocally, copy.Items[1].Installed, copy.Items[1].Latest));
        Assert.Equal(ErrorCode.WorkshopManifestUnreadable, Assert.Single(copy.Warnings).Code);
    }

    [Fact]
    public void WorkshopModSubscriptions_RoundTrips()
    {
        var item = new WorkshopItem(3811390574, DateTimeOffset.FromUnixTimeSeconds(1792336673), false, null, null, WorkshopItemState.NotInstalled);
        var original = new WorkshopModSubscriptions(
            [new WorkshopModItem(item, [new InstalledMod("ModA", "Mod A", 3811390574, InstalledModSource.SteamWorkshop, [], [], [], ["ModB"])], ["pack"])],
            [new Error(ErrorCode.ModCollectionCacheFolderUnresolved, "msg", ErrorKind.NotFound) { Details = new Dictionary<string, string> { ["path"] = "p" } }]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.WorkshopModSubscriptions);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.WorkshopModSubscriptions);

        Assert.NotNull(copy);
        var joined = Assert.Single(copy.Items);
        Assert.Equal(item, joined.Item);
        Assert.Equal(["ModB"], Assert.Single(joined.Mods).Incompatible);
        Assert.Equal(["pack"], joined.Collections);
        Assert.Equal(ErrorCode.ModCollectionCacheFolderUnresolved, Assert.Single(copy.Warnings).Code);
    }

    [Fact]
    public void WorkshopIdKinds_RoundTrip()
    {
        IReadOnlyList<WorkshopIdKind> original = [new WorkshopIdKind(3623026433, true, "Pack"), new WorkshopIdKind(18446744073709551615, false, null)];

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.IReadOnlyListWorkshopIdKind);

        Assert.Equal(original, JsonSerializer.Deserialize(json, CoreJsonContext.Default.IReadOnlyListWorkshopIdKind));
    }

    [Fact]
    public void SharedWorkshopFavorites_RoundTrips()
    {
        var original = new SharedWorkshopFavorites([9000], [1001, 18446744073709551615], [42], 2);

        var copy = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, CoreJsonContext.Default.SharedWorkshopFavorites), CoreJsonContext.Default.SharedWorkshopFavorites);

        Assert.NotNull(copy);
        Assert.Equal(original.Collections, copy.Collections);
        Assert.Equal(original.Items, copy.Items);
        Assert.Equal(original.Unmarked, copy.Unmarked);
        Assert.Equal(2, copy.SkippedLines);
    }

    [Fact]
    public void WorkshopCollection_RoundTrips()
    {
        var original = new WorkshopCollection(
            3814445384,
            "Example pack",
            [
                new WorkshopCollectionItem(1, new WorkshopItemDetails(1, "One", "11", DateTimeOffset.FromUnixTimeSeconds(5), 7)),
                new WorkshopCollectionItem(2, null),
            ]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.WorkshopCollection);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.WorkshopCollection);

        Assert.NotNull(copy);
        Assert.Equal((original.Id, original.Title), (copy.Id, copy.Title));
        Assert.Equal(original.Items, copy.Items);
    }

    [Fact]
    public void WorkshopActionRequestAndOutcome_RoundTrip()
    {
        var request = new WorkshopActionRequest(WorkshopAction.Unsubscribe, [3811390574, 2409333430]);
        var outcome = new WorkshopActionOutcome(
            [new WorkshopItemOutcome(3811390574, null, 0), new WorkshopItemOutcome(2409333430, WorkshopItemFailure.TimedOut, 0)]);

        var requestJson = JsonSerializer.Serialize(request, CoreJsonContext.Default.WorkshopActionRequest);
        var outcomeJson = JsonSerializer.Serialize(outcome, CoreJsonContext.Default.WorkshopActionOutcome);
        var requestCopy = JsonSerializer.Deserialize(requestJson, CoreJsonContext.Default.WorkshopActionRequest);
        var outcomeCopy = JsonSerializer.Deserialize(outcomeJson, CoreJsonContext.Default.WorkshopActionOutcome);

        Assert.Contains("\"action\":\"Unsubscribe\"", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"failure\":\"TimedOut\"", outcomeJson, StringComparison.Ordinal);
        Assert.NotNull(requestCopy);
        Assert.Equal(request.Action, requestCopy.Action);
        Assert.Equal(request.Ids, requestCopy.Ids);
        Assert.NotNull(outcomeCopy);
        Assert.Equal(outcome.Items, outcomeCopy.Items);
    }

    [Fact]
    public void WorkshopUserCollections_RoundTrips()
    {
        var original = new WorkshopUserCollections(
            12345678,
            [new WorkshopCollectionSummary(3623026433, "Example pack", 201), new WorkshopCollectionSummary(18446744073709551615, null, 0)],
            [new WorkshopCollectionSummary(3815132834, "Mine", 214)]);

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.WorkshopUserCollections);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.WorkshopUserCollections);

        Assert.Contains("\"childCount\":201", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal(original.AccountId, copy.AccountId);
        Assert.Equal(original.Favorited, copy.Favorited);
        Assert.Equal(original.Published, copy.Published);
    }

    [Fact]
    public void WorkshopActionEvent_RoundTrips_WithItsKind()
    {
        WorkshopActionEvent[] events =
        [
            new WorkshopActionWaiting(),
            new WorkshopActionStarted(),
            new WorkshopItemProgress(1, 10, 100),
            new WorkshopItemFinished(new WorkshopItemOutcome(1, WorkshopItemFailure.DownloadFailed, 15)),
        ];

        foreach (var e in events)
        {
            var json = JsonSerializer.Serialize(e, CoreJsonContext.Default.WorkshopActionEvent);

            Assert.StartsWith("{\"event\":", json, StringComparison.Ordinal);
            Assert.Equal(e, JsonSerializer.Deserialize(json, CoreJsonContext.Default.WorkshopActionEvent));
        }
    }
}
