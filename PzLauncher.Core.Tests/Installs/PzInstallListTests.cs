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
    public void Merge_DisjointLists_KeepsPrimaryThenSecondaryOrder()
    {
        var merged = PzInstallList.Merge([SteamServer, SteamGame], [ManualServer], StringComparison.Ordinal);

        Assert.Equal([SteamServer, SteamGame, ManualServer], merged);
    }

    [Fact]
    public void Merge_SamePathIgnoringCase_KeepsPrimaryEntry()
    {
        var merged = PzInstallList.Merge([SteamGame], [ManualGameOtherCasing, ManualServer], StringComparison.OrdinalIgnoreCase);

        Assert.Equal([SteamGame, ManualServer], merged);
    }

    [Fact]
    public void Merge_CaseSensitiveComparison_KeepsBothSpellings()
    {
        var merged = PzInstallList.Merge([SteamGame], [ManualGameOtherCasing], StringComparison.Ordinal);

        Assert.Equal([SteamGame, ManualGameOtherCasing], merged);
    }

    [Fact]
    public void Merge_DuplicatesWithinPrimary_KeepsFirst()
    {
        var merged = PzInstallList.Merge([SteamGame, SteamGame with { Steam = new SteamAppInfo("101", null) }], [], StringComparison.Ordinal);

        Assert.Equal([SteamGame], merged);
    }

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, true)]
    [InlineData(StringComparison.Ordinal, false)]
    public void ContainsPath_OtherCasing_DependsOnComparison(StringComparison comparison, bool expected)
    {
        Assert.Equal(expected, PzInstallList.ContainsPath([SteamGame], ManualGameOtherCasing.RootPath, comparison));
    }

    [Fact]
    public void ContainsPath_UnknownPath_IsFalse()
    {
        Assert.False(PzInstallList.ContainsPath([SteamGame], ManualServer.RootPath, StringComparison.OrdinalIgnoreCase));
    }
}
