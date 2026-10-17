using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy;

public class ZombieBuddyAgentArgsTests
{
    private const string Native = "-agentlib:zbNative";
    private const string Java = "-javaagent:ZombieBuddy.jar";
    private const string Package = "com.example.skip";
    private const string Entry = "Skip.jar:" + Package;

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

    [Theory]
    [InlineData("", "=patches_jar=" + Entry)]
    [InlineData("=policy=allow-all", "=policy=allow-all,patches_jar=" + Entry)]
    [InlineData("=patches_jar=other.jar:x.y,policy=allow-all", "=patches_jar=other.jar:x.y;" + Entry + ",policy=allow-all")]
    [InlineData("=patches_jar=Old.jar:" + Package + ";other.jar:x.y", "=patches_jar=" + Entry + ";other.jar:x.y")]
    [InlineData(@"=patches_jar=other.jar:x.y;E:\mods\Skip.jar:" + Package, "=patches_jar=other.jar:x.y;" + Entry)]
    [InlineData("=patches_jar=a.jar:" + Package + ";other.jar:x.y;b.jar: " + Package + " ", "=patches_jar=" + Entry + ";other.jar:x.y")]
    [InlineData("=patches_jar=", "=patches_jar=" + Entry)]
    [InlineData("=PATCHES_JAR=other.jar:x.y", "=PATCHES_JAR=other.jar:x.y;" + Entry)]
    [InlineData("=patches_jar=a.jar:x.y,patches_jar=b.jar:z", "=patches_jar=a.jar:x.y,patches_jar=b.jar:z;" + Entry)]
    public void WithPatchesJar_AgentOptions_HoldsOneEntryForThePackage(string before, string after) =>
        Assert.Equal(["-Xmx3072m", Native + after], ZombieBuddyAgentArgs.WithPatchesJar(["-Xmx3072m", Native + before], PlatformKind.Windows, "Skip.jar", Package));

    [Theory]
    [InlineData("=policy=allow-all,patches_jar=" + Entry)]
    [InlineData("=patches_jar=other.jar:x.y;" + Entry + ",verbosity=1")]
    public void WithPatchesJar_AlreadyThere_ReturnsTheSameInstance(string options)
    {
        string[] vmArgs = ["-Xmx3072m", Native + options];

        Assert.Same(vmArgs, ZombieBuddyAgentArgs.WithPatchesJar(vmArgs, PlatformKind.Windows, "Skip.jar", Package));
    }

    [Theory]
    [InlineData("=policy=allow-all,patches_jar=" + Entry, "=policy=allow-all")]
    [InlineData("=patches_jar=" + Entry, "")]
    [InlineData("=patches_jar=other.jar:x.y;" + Entry + ";last.jar:z,verbosity=1", "=patches_jar=other.jar:x.y;last.jar:z,verbosity=1")]
    [InlineData(@"=patches_jar=E:\mods\Skip.jar:" + Package + ";Old.jar:" + Package + ",policy=prompt", "=policy=prompt")]
    [InlineData("=patches_jar=" + Entry + ",patches_jar=other.jar:x.y;" + Entry, "=patches_jar=" + Entry + ",patches_jar=other.jar:x.y")]
    [InlineData("=patches_jar=other.jar:x.y,patches_jar=" + Entry + ",policy=prompt", "=patches_jar=other.jar:x.y,patches_jar=,policy=prompt")]
    public void WithoutPatchesJar_AgentOptions_DropsThePackagesEntries(string before, string after) =>
        Assert.Equal([Native + after, "-Xmx3072m"], ZombieBuddyAgentArgs.WithoutPatchesJar([Native + before, "-Xmx3072m"], PlatformKind.Windows, Package));

    [Theory]
    [InlineData("")]
    [InlineData("=policy=allow-all")]
    [InlineData("=patches_jar=other.jar:x.y;Skip.jar:com.example.skip.other")]
    [InlineData("=patches_jar=,,policy=prompt")]
    [InlineData("=patches_jar=other.jar:x.y;;last.jar:z")]
    public void WithoutPatchesJar_NoEntryForThePackage_ReturnsTheSameInstance(string options)
    {
        string[] vmArgs = [Native + options];

        Assert.Same(vmArgs, ZombieBuddyAgentArgs.WithoutPatchesJar(vmArgs, PlatformKind.Windows, Package));
    }

    [Fact]
    public void PatchesJar_LinuxAgentArg_EditsTheJavaAgent() =>
        Assert.Equal(
            [Java + "=patches_jar=" + Entry],
            ZombieBuddyAgentArgs.WithPatchesJar([Java], PlatformKind.Linux, "Skip.jar", Package));

    [Fact]
    public void PatchesJar_NoAgentArg_ReturnsTheSameInstance()
    {
        string[] vmArgs = ["-Xmx3072m", Java];

        Assert.Same(vmArgs, ZombieBuddyAgentArgs.WithPatchesJar(vmArgs, PlatformKind.Windows, "Skip.jar", Package));
        Assert.Same(vmArgs, ZombieBuddyAgentArgs.WithoutPatchesJar(vmArgs, PlatformKind.Windows, Package));
    }
}
