using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Serialization;

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
                new JavaRuntime(@"C:\Java\jdk-21", @"C:\Java\jdk-21\bin\javaw.exe", new JavaVersion(21, "21.0.9"), "Eclipse Adoptium", JavaRuntimeSource.Registry, HasGraalCompiler: false),
                new JavaRuntime("/opt/jdk", "/opt/jdk/bin/java", Version: null, Vendor: null, JavaRuntimeSource.Manual, HasGraalCompiler: true),
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
    [InlineData("""{"homePath":null,"executablePath":"/x/bin/java","version":null,"vendor":null,"source":"Manual","hasGraalCompiler":false}""")]
    [InlineData("""{"executablePath":"/x/bin/java","version":null,"vendor":null,"source":"Manual","hasGraalCompiler":false}""")]
    [InlineData("""{"homePath":"/x","executablePath":"/x/bin/java","version":{"major":21,"full":null},"vendor":null,"source":"Manual","hasGraalCompiler":false}""")]
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
            new JavaRuntime("/games/pz/jre64", "/games/pz/jre64/bin/java", new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaRuntimeSource.Manual, HasGraalCompiler: false),
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
}
