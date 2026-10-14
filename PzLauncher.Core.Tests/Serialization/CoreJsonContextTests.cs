using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
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
}
