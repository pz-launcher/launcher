using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Modpacks;

namespace PzLauncher.Core.Tests.Mod.Modpacks;

public sealed class ModpackExtraItemsTests
{
    private static readonly InstalledMod[] Installed =
    [
        Mod("Core", workshopId: 1001),
        Mod("CoreExtras", workshopId: 1001),
        Mod("Maps", workshopId: 1002),
        Mod("Cars", workshopId: 1003),
        Mod("Tweaks", source: InstalledModSource.Local),
    ];

    private static InstalledMod Mod(string id, ulong? workshopId = null, InstalledModSource source = InstalledModSource.SteamWorkshop) =>
        new(id, id, workshopId, source, [], [], [], []);

    private static IReadOnlyList<ulong> Compute(ulong[] stored, string[] preset, params ulong[] collection) =>
        ModpackExtraItems.Compute(stored, preset, Installed, collection);

    [Fact]
    public void NothingStored_TakesTheItemsOfInstalledPresetModsInPresetOrderEachOnce() =>
        Assert.Equal([1003UL, 1001], Compute([], ["Cars", "Tweaks", "Core", "Gone", "CoreExtras"]));

    [Fact]
    public void ItemsTheCollectionHolds_AreLeftOut() =>
        Assert.Equal([1003UL], Compute([1001], ["Core", "Cars"], 1001, 1002));

    [Fact]
    public void StoredItemNotInstalled_IsKeptInItsPlace() =>
        Assert.Equal([2001UL, 1003, 1001], Compute([2001, 1003], ["Core", "Cars"]));

    [Fact]
    public void StoredInstalledItemWithNoModInThePresetAnyMore_IsDropped() =>
        Assert.Equal([1003UL], Compute([1002, 1003], ["Cars"]));

    [Fact]
    public void StoredInstalledItemWithOneModStillInThePreset_IsKept() =>
        Assert.Equal([1001UL], Compute([1001], ["CoreExtras"]));

    [Fact]
    public void StoredItemTheCollectionNowHolds_IsDroppedEvenIfNotInstalled() =>
        Assert.Equal([1003UL], Compute([2001, 1003], ["Cars"], 2001));

    [Fact]
    public void UnchangedInput_GivesTheStoredList() =>
        Assert.Equal([2001UL, 1001, 1003], Compute([2001, 1001, 1003], ["Core", "Cars"]));

    [Fact]
    public void Summarize_CountsPresetModsNotInstalledAndNotFromTheWorkshop()
    {
        var summary = ModpackExtraItems.Summarize([2001], ["Cars", "Tweaks", "Gone", "Core", "Missing"], Installed, [1001]);

        Assert.Equal([2001UL, 1003], summary.ExtraItems);
        Assert.Equal((2, 1), (summary.NotInstalled, summary.NotWorkshop));
    }

    [Fact]
    public void Summarize_ModInstalledTwice_CountsTheCopyTheGameLoads()
    {
        InstalledMod[] installed = [Mod("Cars", source: InstalledModSource.Local), Mod("Cars", workshopId: 1003)];

        var summary = ModpackExtraItems.Summarize([], ["Cars"], installed, []);

        Assert.Empty(summary.ExtraItems);
        Assert.Equal((0, 1), (summary.NotInstalled, summary.NotWorkshop));
    }
}
