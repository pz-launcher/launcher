using PzLauncher.Core.Installs;

namespace PzLauncher.Core.Tests.Installs;

public class PzInstallListTests
{
    private static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

    private static readonly PzInstall SteamServer =
        new(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Server", new SteamAppInfo("200", null));

    private static readonly PzInstall ManualGameOtherCasing =
        new(PzInstallKind.Game, @"e:\steamlibrary\steamapps\common\projectzomboid", Steam: null);

    private static readonly PzInstall ManualServer =
        new(PzInstallKind.DedicatedServer, @"D:\Servers\PZ", Steam: null);

    [Fact]
    public void Merge_DisjointLists_KeepsPrimaryThenSecondaryOrder() =>
        Assert.Equal([SteamServer, SteamGame, ManualServer], PzInstallList.Merge([SteamServer, SteamGame], [ManualServer], StringComparison.Ordinal));

    [Fact]
    public void Merge_SamePathIgnoringCase_KeepsPrimaryEntry() =>
        Assert.Equal([SteamGame, ManualServer], PzInstallList.Merge([SteamGame], [ManualGameOtherCasing, ManualServer], StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Merge_CaseSensitiveComparison_KeepsBothSpellings() =>
        Assert.Equal([SteamGame, ManualGameOtherCasing], PzInstallList.Merge([SteamGame], [ManualGameOtherCasing], StringComparison.Ordinal));

    [Fact]
    public void Merge_DuplicatesWithinPrimary_KeepsFirst() =>
        Assert.Equal([SteamGame], PzInstallList.Merge([SteamGame, SteamGame with { Steam = new SteamAppInfo("101", null) }], [], StringComparison.Ordinal));

    [Theory]
    [InlineData(@"e:\steamlibrary\steamapps\common\projectzomboid", StringComparison.OrdinalIgnoreCase, true)]
    [InlineData(@"e:\steamlibrary\steamapps\common\projectzomboid", StringComparison.Ordinal, false)]
    [InlineData(@"D:\Servers\PZ", StringComparison.OrdinalIgnoreCase, false)]
    public void ContainsPath_PathAndComparison_MatchesListedRoots(string path, StringComparison comparison, bool expected) =>
        Assert.Equal(expected, PzInstallList.ContainsPath([SteamGame], path, comparison));
}
