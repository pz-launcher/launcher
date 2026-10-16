using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Launch;

public sealed class PzLaunchConfigTests : IDisposable
{
    private const string ConfigPath = "/games/pz/ProjectZomboid64.json";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PzLaunchConfig Parse(string json) => ResultAssert.Ok(PzLaunchConfig.Parse(json, ConfigPath));

    [Fact]
    public void Parse_WindowsGameFixture_ReadsAllFields()
    {
        var config = Parse(GameInstallBuilder.WindowsGameJson);

        Assert.Equal("zombie/gameStates/MainScreenState", config.MainClass);
        Assert.Equal([".", "projectzomboid.jar"], config.Classpath);
        Assert.Equal(9, config.VmArgs.Count);
        Assert.Contains("-Xmx3072m", config.VmArgs);
        Assert.Equal(["10.0.17134", "6.1"], config.Windows.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Parse_LinuxGameFixture_HasNoOsVersionMap()
    {
        var config = Parse(GameInstallBuilder.LinuxGameJson);

        Assert.Empty(config.Windows);
        Assert.Equal(config.VmArgs, config.JvmArgs(PlatformKind.Linux, new Version(6, 8)));
    }

    [Theory]
    [InlineData("10.0.26100.0", "-XX:+UseZGC")]
    [InlineData("10.0.17134", "-XX:+UseZGC")]
    [InlineData("10.0.17133", "-XX:+UseG1GC")]
    [InlineData("6.1.7601", "-XX:+UseG1GC")]
    [InlineData("6.0.6002", null)]
    public void WindowsVmArgs_GameFixture_PicksHighestVersionNotAboveOs(string osVersion, string? expected) =>
        Assert.Equal(
            expected is null ? [] : new[] { expected },
            Parse(GameInstallBuilder.WindowsGameJson).WindowsVmArgs(Version.Parse(osVersion)));

    [Theory]
    [InlineData("10.0.19045.0", "-XX:+UseZGC")]
    [InlineData("7.0", "-XX:+UseG1GC")]
    [InlineData("6.3.9600", null)]
    public void WindowsVmArgs_ServerFixtureBareMajorKeys_ReadAsMajorDotZero(string osVersion, string? expected) =>
        Assert.Equal(
            expected is null ? [] : new[] { expected },
            Parse(GameInstallBuilder.ServerJson).WindowsVmArgs(Version.Parse(osVersion)));

    [Fact]
    public void WindowsVmArgs_UnparseableKey_IsIgnored()
    {
        var config = Parse("""{"mainClass":"a/B","classpath":["."],"windows":{"vista":{"vmArgs":["-X1"]},"6.1":{"vmArgs":["-X2"]}}}""");

        Assert.Equal(["-X2"], config.WindowsVmArgs(new Version(10, 0)));
    }

    [Theory]
    [InlineData(PlatformKind.Windows, new[] { "-Xmx3072m", "-XX:+UseZGC" })]
    [InlineData(PlatformKind.Linux, new[] { "-Xmx3072m" })]
    public void JvmArgs_WindowsMap_AppliedOnlyOnWindows(PlatformKind os, string[] expected)
    {
        var config = Parse("""{"mainClass":"a/B","classpath":["."],"vmArgs":["-Xmx3072m"],"windows":{"10.0":{"vmArgs":["-XX:+UseZGC"]}}}""");

        Assert.Equal(expected, config.JvmArgs(os, new Version(10, 0, 26100, 0)));
    }

    [Fact]
    public void Parse_CommentsTrailingCommasAndNameCase_AreAccepted()
    {
        var config = Parse("""
            {
              // edited by hand
              "MainClass": "a/B",
              "CLASSPATH": [".", "x.jar",],
              "vmArgs": ["-Xmx1g", null, ""],
            }
            """);

        Assert.Equal("a/B", config.MainClass);
        Assert.Equal([".", "x.jar"], config.Classpath);
        Assert.Equal(["-Xmx1g"], config.VmArgs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"mainClass":"a/B","classpath":"."}""")]
    [InlineData("""{"classpath":["."]}""")]
    [InlineData("""{"mainClass":" ","classpath":["."]}""")]
    [InlineData("""{"mainClass":"a/B"}""")]
    [InlineData("""{"mainClass":"a/B","classpath":[]}""")]
    [InlineData("""{"mainClass":"a/B","classpath":["."],"windows":{"6.1":{"vmArgs":[1]}}}""")]
    public void Parse_Malformed_ReturnsConfigMalformed(string json)
    {
        var error = ResultAssert.Failed(PzLaunchConfig.Parse(json, ConfigPath), ErrorCode.LaunchConfigMalformed);

        Assert.Equal(ConfigPath, error.Details["path"]);
    }

    [Fact]
    public void Parse_DeeplyNested_ReturnsConfigMalformed()
    {
        var json = $$"""{"mainClass":"a/B","classpath":["."],"x":{{new string('[', 10_000)}}{{new string(']', 10_000)}}}""";

        ResultAssert.Failed(PzLaunchConfig.Parse(json, ConfigPath), ErrorCode.LaunchConfigMalformed);
    }

    [Theory]
    [InlineData("missing.json")]
    [InlineData("nofolder", "ProjectZomboid64.json")]
    public async Task ReadAsync_Missing_ReturnsConfigMissing(params string[] parts)
    {
        var path = Path.Combine([_temp.Path, .. parts]);

        var error = ResultAssert.Failed(await PzLaunchConfig.ReadAsync(path, Ct), ErrorCode.LaunchConfigMissing);

        Assert.Equal(path, error.Details["path"]);
    }

    [Fact]
    public async Task ReadAsync_PathIsFolder_ReturnsConfigMalformed()
    {
        var path = _temp.CreateDirectory("ProjectZomboid64.json");

        ResultAssert.Failed(await PzLaunchConfig.ReadAsync(path, Ct), ErrorCode.LaunchConfigMalformed);
    }
}
