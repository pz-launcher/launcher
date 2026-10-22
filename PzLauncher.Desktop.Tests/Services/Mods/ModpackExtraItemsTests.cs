using PzLauncher.Core.Mod;
using PzLauncher.Desktop.Services.Mods;
using static PzLauncher.Desktop.Tests.TestSupport.TestMods;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class ModpackExtraItemsTests
{
    private static readonly InstalledMod[] Installed =
    [
        Mod("Core", workshopId: 1001),
        Mod("CoreExtras", workshopId: 1001),
        Mod("Maps", workshopId: 1002),
        Mod("Cars", workshopId: 1003),
        Mod("Tweaks", source: InstalledModSource.Local),
    ];

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
}
