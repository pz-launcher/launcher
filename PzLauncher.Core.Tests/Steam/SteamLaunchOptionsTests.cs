using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Tests.Steam;

public class SteamLaunchOptionsTests
{
    private const string Exe = @"C:\Games\PzLauncher\PzLauncher.exe";
    private const string Hook = "\"" + Exe + "\" %command%";
    private const string OldExe = @"D:\Old Launcher\PzLauncher.exe";

    [Theory]
    [InlineData("", Hook)]
    [InlineData("   ", Hook)]
    [InlineData("-debug", Hook + " -debug")]
    [InlineData("-agentlib:zbNative --", Hook + " -agentlib:zbNative --")]
    [InlineData("ENV=1 %command% -debug", "ENV=1 " + Hook + " -debug")]
    [InlineData("%command% -debug", Hook + " -debug")]
    [InlineData("\"" + OldExe + "\" %command% -debug", Hook + " -debug")]
    [InlineData("ENV=1 \"" + OldExe + "\"  %command%", "ENV=1 " + Hook)]
    public void AddHook_KeepsTheUsersOptions(string options, string expected) =>
        Assert.Equal(expected, SteamLaunchOptions.AddHook(options, Exe));

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("-debug", "-debug")]
    [InlineData("-agentlib:zbNative --", "-agentlib:zbNative --")]
    [InlineData("ENV=1 %command% -debug", "ENV=1 %command% -debug")]
    [InlineData("%command% -debug", "-debug")]
    public void RemoveHook_AfterAddHook_RestoresTheOptionsMeaning(string options, string expected) =>
        Assert.Equal(expected, SteamLaunchOptions.RemoveHook(SteamLaunchOptions.AddHook(options, Exe), Exe));

    [Theory]
    [InlineData("\"" + OldExe + "\" %command% -debug", "-debug")]
    [InlineData("-x \"" + Exe + "\" %command% \"" + OldExe + "\" %command%", "-x %command% %command%")]
    [InlineData("-debug", "-debug")]
    [InlineData("\"C:\\Tools\\wrapper.exe\" %command%", "\"C:\\Tools\\wrapper.exe\" %command%")]
    public void RemoveHook_RemovesEveryLauncherHookOnly(string options, string expected) =>
        Assert.Equal(expected, SteamLaunchOptions.RemoveHook(options, Exe));

    [Theory]
    [InlineData("", null)]
    [InlineData("-debug", null)]
    [InlineData(Hook, Exe)]
    [InlineData("\"" + OldExe + "\" %command%", OldExe)]
    [InlineData("\"C:\\games\\pzlauncher.EXE\"\t%command%", @"C:\games\pzlauncher.EXE")]
    [InlineData("-name \"my game\" \"" + OldExe + "\" %command%", OldExe)]
    [InlineData("\"/opt/pzlauncher/PzLauncher.exe\" %command%", "/opt/pzlauncher/PzLauncher.exe")]
    [InlineData("\"PzLauncher.exe\" %command%", null)]
    [InlineData("\"" + Exe + "\" -debug", null)]
    [InlineData("\"" + Exe + "\"%command%", null)]
    [InlineData("\"C:\\Tools\\wrapper.exe\" %command%", null)]
    [InlineData("\"" + Exe, null)]
    public void FindHookPath_FindsAQuotedLauncherBeforeCommand(string options, string? expected) =>
        Assert.Equal(expected, SteamLaunchOptions.FindHookPath(options, Exe));

    [Fact]
    public void FindHookPath_RenamedLauncher_IsFoundByItsOwnName()
    {
        const string renamed = @"C:\Games\Launcher\MyLauncher.exe";

        Assert.Equal(renamed, SteamLaunchOptions.FindHookPath("\"" + renamed + "\" %command%", renamed));
    }
}
