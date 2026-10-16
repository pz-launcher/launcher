using PzLauncher.Core.Platform;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Tests.ZombieBuddy;

public class ZombieBuddyAgentArgsTests
{
    private const string Native = "-agentlib:zbNative";
    private const string Java = "-javaagent:ZombieBuddy.jar";

    [Theory]
    [InlineData(Native, PlatformKind.Windows, true)]
    [InlineData(Native + "=verbosity=1", PlatformKind.Windows, true)]
    [InlineData(Native + "Other", PlatformKind.Windows, false)]
    [InlineData(Java, PlatformKind.Windows, false)]
    [InlineData(Java, PlatformKind.Linux, true)]
    [InlineData(Java + "=policy=deny-new", PlatformKind.Linux, true)]
    [InlineData(Java + ".new", PlatformKind.Linux, false)]
    [InlineData(Native, PlatformKind.Linux, false)]
    public void IsAgentArg_BareOrWithOptions_MatchesOnlyThisOsForm(string arg, PlatformKind os, bool expected) =>
        Assert.Equal(expected, ZombieBuddyAgentArgs.IsAgentArg(arg, os));

    [Fact]
    public void HasAny_OtherOsFormOnWindows_CountsButHasDoesNot()
    {
        string[] vmArgs = ["-Xmx3072m", Java];

        Assert.True(ZombieBuddyAgentArgs.HasAny(vmArgs));
        Assert.False(ZombieBuddyAgentArgs.Has(vmArgs, PlatformKind.Windows));
    }

    [Theory]
    [InlineData(PlatformKind.Windows, Native)]
    [InlineData(PlatformKind.Linux, Java)]
    public void Add_NoAgentArg_PrependsBareArg(PlatformKind os, string expected) =>
        Assert.Equal([expected, "-Xmx3072m", "-XX:+UseZGC"], ZombieBuddyAgentArgs.Add(["-Xmx3072m", "-XX:+UseZGC"], os));

    [Fact]
    public void Add_AgentArgWithOptions_KeepsItAndAddsNone()
    {
        string[] vmArgs = ["-Xmx3072m", Native + "=policy=allow-all"];

        Assert.Equal(vmArgs, ZombieBuddyAgentArgs.Add(vmArgs, PlatformKind.Windows));
    }

    [Fact]
    public void Remove_EveryFormWithOrWithoutOptions_KeepsTheRestInOrder() =>
        Assert.Equal(
            ["-Xmx3072m", "-Dzomboid.steam=1"],
            ZombieBuddyAgentArgs.Remove([Native + "=verbosity=1", "-Xmx3072m", Java, "-Dzomboid.steam=1", Native]));

    [Theory]
    [InlineData("", false)]
    [InlineData("=policy=allow-all", true)]
    [InlineData("=verbosity=1,policy=allow-all", true)]
    [InlineData("=POLICY=Allow-All", true)]
    [InlineData("=policy=deny-new", false)]
    [InlineData("=policy=allow-all,policy=prompt", false)]
    [InlineData("=policy", false)]
    [InlineData("=verbosity=1", false)]
    public void IsAllowAll_AgentOptions_ReadsEffectivePolicy(string options, bool expected) =>
        Assert.Equal(expected, ZombieBuddyAgentArgs.IsAllowAll(["-Xmx3072m", Native + options], PlatformKind.Windows));

    [Theory]
    [InlineData("", null)]
    [InlineData("=policy=allow-all", null)]
    [InlineData(@"=config_dir=D:\zb", @"D:\zb")]
    [InlineData(@"=CONFIG_DIR= D:\zb ,policy=allow-all", @"D:\zb")]
    [InlineData(@"=config_dir=D:\a,config_dir=D:\b", @"D:\b")]
    [InlineData("=config_dir", "")]
    public void Option_AgentOptions_ReadsTheLastTrimmedValue(string options, string? expected) =>
        Assert.Equal(expected, ZombieBuddyAgentArgs.Option(["-Xmx3072m", Native + options], PlatformKind.Windows, "config_dir"));

    [Fact]
    public void Option_OnlyLaterAgentArgHasIt_IsNull() =>
        Assert.Null(ZombieBuddyAgentArgs.Option([Native, Native + @"=config_dir=D:\zb"], PlatformKind.Windows, "config_dir"));

    [Fact]
    public void IsAllowAll_OnlyLaterAgentArgAllowsAll_IsFalse() =>
        Assert.False(ZombieBuddyAgentArgs.IsAllowAll([Native, Native + "=policy=allow-all"], PlatformKind.Windows));

    [Theory]
    [InlineData(PlatformKind.Windows, "", true, "=policy=allow-all")]
    [InlineData(PlatformKind.Windows, "=verbosity=1", true, "=verbosity=1,policy=allow-all")]
    [InlineData(PlatformKind.Windows, "=policy=deny-new", true, "=policy=allow-all")]
    [InlineData(PlatformKind.Windows, "=policy=deny-new,verbosity=1", true, "=policy=allow-all,verbosity=1")]
    [InlineData(PlatformKind.Windows, "=verbosity=1,policy=deny-new,policy=prompt", true, "=verbosity=1,policy=allow-all")]
    [InlineData(PlatformKind.Windows, "=verbosity=1,policy=allow-all", false, "=verbosity=1")]
    [InlineData(PlatformKind.Windows, "=policy=allow-all", false, "")]
    [InlineData(PlatformKind.Windows, "=policy=deny-new", false, "")]
    [InlineData(PlatformKind.Windows, "=policy=allow-all,verbosity=2,frontend", false, "=verbosity=2,frontend")]
    [InlineData(PlatformKind.Windows, "", false, "")]
    [InlineData(PlatformKind.Linux, "=verbosity=1", true, "=verbosity=1,policy=allow-all")]
    [InlineData(PlatformKind.Linux, "=verbosity=1,policy=allow-all", false, "=verbosity=1")]
    public void WithPolicy_AgentOptions_RewritesOnlyPolicy(PlatformKind os, string before, bool allowAll, string after)
    {
        var agent = ZombieBuddyFiles.AgentArg(os);

        Assert.Equal(["-Xmx3072m", agent + after], ZombieBuddyAgentArgs.WithPolicy(["-Xmx3072m", agent + before], os, allowAll));
    }

    [Fact]
    public void WithPolicy_SeveralAgentArgs_RewritesOnlyTheFirst() =>
        Assert.Equal(
            [Native + "=policy=allow-all", Native + "=verbosity=1"],
            ZombieBuddyAgentArgs.WithPolicy([Native, Native + "=verbosity=1"], PlatformKind.Windows, allowAll: true));

    [Fact]
    public void WithPolicy_NoAgentArg_ReturnsArgsUnchanged()
    {
        string[] vmArgs = ["-Xmx3072m", Java];

        Assert.Equal(vmArgs, ZombieBuddyAgentArgs.WithPolicy(vmArgs, PlatformKind.Windows, allowAll: true));
    }
}
