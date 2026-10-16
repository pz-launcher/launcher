using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.Launch;

public class LaunchArgumentsTests
{
    private static readonly Version Windows11 = new(10, 0, 26100, 0);

    private static PzLaunchConfig Config(params string[] vmArgs) =>
        new("zombie/gameStates/MainScreenState", [".", "projectzomboid.jar"], vmArgs, new Dictionary<string, IReadOnlyList<string>>
        {
            ["10.0.17134"] = ["-XX:+UseZGC"],
        });

    private static GameLaunchOverrides Overrides(int? max = null, int? min = null, string jvm = "", string game = "") =>
        new(null, max, min, jvm, game);

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("-debug", new[] { "-debug" })]
    [InlineData("  -a   -b\t-c ", new[] { "-a", "-b", "-c" })]
    [InlineData("-Dname=\"a b\" -x", new[] { "-Dname=a b", "-x" })]
    [InlineData("\"C:\\My Mods\\x\"", new[] { "C:\\My Mods\\x" })]
    [InlineData("-a \"\" -b", new[] { "-a", "", "-b" })]
    [InlineData("-a \"unclosed  quote", new[] { "-a", "unclosed  quote" })]
    public void Split_QuotesGroupWhitespaceSeparates(string text, string[] expected) =>
        Assert.Equal(expected, LaunchArguments.Split(text));

    [Theory]
    [InlineData("3072m", 3072)]
    [InlineData("3072M", 3072)]
    [InlineData("3g", 3072)]
    [InlineData("3G", 3072)]
    [InlineData("2097152k", 2048)]
    [InlineData("2097152K", 2048)]
    [InlineData("3221225472", 3072)]
    [InlineData("1536k", 1)]
    [InlineData("512k", null)]
    [InlineData("", null)]
    [InlineData("m", null)]
    [InlineData("-1g", null)]
    [InlineData("3t", null)]
    [InlineData("1.5g", null)]
    [InlineData("99999999999g", null)]
    [InlineData("99999999999999999999", null)]
    public void ParseSizeMb_JvmSizeSyntax_ReturnsWholeMegabytes(string size, int? expected) =>
        Assert.Equal(expected, LaunchArguments.ParseSizeMb(size));

    [Fact]
    public void HeapSizeMb_SeveralOptions_LastWins() =>
        Assert.Equal(4096, LaunchArguments.HeapSizeMb(["-Xmx2g", "-Xms1g", "-Xmx4g"], LaunchArguments.MaxHeapPrefix));

    [Fact]
    public void HeapSizeMb_Absent_ReturnsNull() =>
        Assert.Null(LaunchArguments.HeapSizeMb(["-Xmx2g"], LaunchArguments.InitialHeapPrefix));

    [Fact]
    public void Build_Windows_OrdersJsonExtraJvmClasspathMainClassGameArgs()
    {
        var args = LaunchArguments.Build(
            Config("-Djava.awt.headless=true", "-Xmx3072m"), PlatformKind.Windows, Windows11, Overrides(jvm: "-Dx=\"a b\"", game: "-debug -nosteam"));

        Assert.Equal(
            ["-Djava.awt.headless=true", "-Xmx3072m", "-XX:+UseZGC", "-Dx=a b",
             "-cp", ".;projectzomboid.jar", "zombie.gameStates.MainScreenState", "-debug", "-nosteam"],
            args);
    }

    [Fact]
    public void Build_Linux_JoinsClasspathWithColonAndSkipsWindowsMap() =>
        Assert.Equal(
            ["-Xmx3072m", "-cp", ".:projectzomboid.jar", "zombie.gameStates.MainScreenState"],
            LaunchArguments.Build(Config("-Xmx3072m"), PlatformKind.Linux, new Version(6, 8), Overrides()));

    [Theory]
    [InlineData("-Xmx3072m")]
    [InlineData("-Xmx3g")]
    [InlineData("-Xmx3221225472")]
    public void Build_MemoryOverrides_ReplaceEveryJsonValueInAnyUnit(string jsonMax)
    {
        var args = LaunchArguments.Build(
            Config("-Xms512m", jsonMax, "-Dx", "-Xmx1g", "-Xms1g"), PlatformKind.Linux, new Version(6, 8), Overrides(max: 8192, min: 4096));

        Assert.Equal(["-Dx", "-Xmx8192m", "-Xms4096m"], args[..^3]);
    }

    [Fact]
    public void Build_MemoryOverridesWithoutJsonValues_AreAdded() =>
        Assert.Equal(
            ["-Dx", "-Xmx8192m", "-Xms2048m"],
            LaunchArguments.Build(Config("-Dx"), PlatformKind.Linux, new Version(6, 8), Overrides(max: 8192, min: 2048))[..^3]);

    [Fact]
    public void Build_NoMemoryOverrides_KeepsJsonValues() =>
        Assert.Equal(
            ["-Xmx3g", "-Xms1g"],
            LaunchArguments.Build(Config("-Xmx3g", "-Xms1g"), PlatformKind.Linux, new Version(6, 8), Overrides())[..^3]);
}
