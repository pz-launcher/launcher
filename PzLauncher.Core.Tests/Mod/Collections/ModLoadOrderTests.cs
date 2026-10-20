using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Core.Tests.Mod.Collections;

public class ModLoadOrderTests
{
    private static InstalledMod Mod(
        string id, string[]? require = null, string[]? loadAfter = null, string[]? loadBefore = null, string[]? incompatible = null) =>
        new(id, id, null, InstalledModSource.Local, require ?? [], loadAfter ?? [], loadBefore ?? [], incompatible ?? []);

    [Fact]
    public void Resolve_RequirementsChain_PutsEachRequirementBeforeItsDependent()
    {
        InstalledMod[] mods = [Mod("A"), Mod("B", require: ["A"]), Mod("C", require: ["B"])];

        var result = ModLoadOrder.Resolve(["C", "A"], mods);

        Assert.Equal(["A", "B", "C"], result.EnabledIds);
        Assert.Empty(result.MissingIds);
        Assert.Empty(result.SkippedIncompatible);
        Assert.False(result.DropsAny);
    }

    [Fact]
    public void Resolve_NotInstalledIds_AreMissingOnceAndANotInstalledRequirementIsIgnored()
    {
        var result = ModLoadOrder.Resolve(["X", "A", "X"], [Mod("A", require: ["NotInstalled"])]);

        Assert.Equal(["A"], result.EnabledIds);
        Assert.Equal(["X"], result.MissingIds);
        Assert.True(result.DropsAny);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_IncompatibleEitherWay_SkipsTheLaterModNamingTheEnabledOne(bool declaredByLater)
    {
        InstalledMod[] mods = declaredByLater
            ? [Mod("A"), Mod("B", incompatible: ["A"])]
            : [Mod("A", incompatible: ["B"]), Mod("B")];

        var result = ModLoadOrder.Resolve(["A", "B"], mods);

        Assert.Equal(["A"], result.EnabledIds);
        Assert.Equal([new ModConflict("B", "A")], result.SkippedIncompatible);
    }

    [Fact]
    public void Resolve_SkippedRequirement_DoesNotBlockItsDependent()
    {
        InstalledMod[] mods = [Mod("A"), Mod("B", incompatible: ["A"]), Mod("C", require: ["B"])];

        var result = ModLoadOrder.Resolve(["A", "C"], mods);

        Assert.Equal(["A", "C"], result.EnabledIds);
        Assert.Equal([new ModConflict("B", "A")], result.SkippedIncompatible);
    }

    [Fact]
    public void Resolve_RequirementIncompatibleWithItsDependent_SkipsTheRequirement()
    {
        var result = ModLoadOrder.Resolve(["M"], [Mod("M", require: ["D"]), Mod("D", incompatible: ["M"])]);

        Assert.Equal(["M"], result.EnabledIds);
        Assert.Equal([new ModConflict("D", "M")], result.SkippedIncompatible);
    }

    [Fact]
    public void Resolve_RequireCycle_EmitsEachModOnce() =>
        Assert.Equal(["B", "A"], ModLoadOrder.Resolve(["A", "B"], [Mod("A", require: ["B"]), Mod("B", require: ["A"])]).EnabledIds);

    [Fact]
    public void Resolve_VeryDeepRequireChain_DoesNotOverflowTheStack()
    {
        const int depth = 100_000;
        var mods = Enumerable.Range(0, depth).Select(i => Mod($"M{i}", require: i + 1 < depth ? [$"M{i + 1}"] : null)).ToList();

        var result = ModLoadOrder.Resolve(["M0"], mods);

        Assert.Equal(depth, result.EnabledIds.Count);
        Assert.Equal($"M{depth - 1}", result.EnabledIds[0]);
        Assert.Equal("M0", result.EnabledIds[^1]);
    }

    [Fact]
    public void AutoSort_NoRules_SortsByOrdinalId() =>
        Assert.Equal(["B", "C", "a"], ModLoadOrder.AutoSort(["C", "a", "B"], [Mod("a"), Mod("B"), Mod("C")]));

    // Hand-run of the game's autoSort: B and its requirements are placed when B's turn comes.
    [Fact]
    public void AutoSort_RequireAndLoadAfter_GoBeforeTheMod() =>
        Assert.Equal(
            ["A", "Z", "M"],
            ModLoadOrder.AutoSort(["M", "A", "Z"], [Mod("Z", loadBefore: ["A"]), Mod("A"), Mod("M", loadAfter: ["Z"])]));

    // The game ignores a loadBefore whose target it has already placed.
    [Fact]
    public void AutoSort_LoadBeforeOfAnAlreadyPlacedMod_IsIgnoredLikeTheGame() =>
        Assert.Equal(["A", "B", "C"], ModLoadOrder.AutoSort(["A", "B", "C"], [Mod("A"), Mod("B", require: ["A"]), Mod("C", loadBefore: ["A"])]));

    // A's loadBefore places B early; B's requirements then go in front of it one at a time, so reversed.
    [Fact]
    public void AutoSort_RequirementsOfAnAlreadyPlacedMod_AreInsertedReversedLikeTheGame() =>
        Assert.Equal(
            ["A", "Y", "X", "B"],
            ModLoadOrder.AutoSort(["A", "B", "X", "Y"], [Mod("A", loadBefore: ["B"]), Mod("B", require: ["X", "Y"]), Mod("X"), Mod("Y")]));

    [Fact]
    public void AutoSort_LoadBeforeOfAnAlreadyPlacedMod_IsInsertedReversedAfterIt() =>
        Assert.Equal(
            ["A", "B", "Y", "X"],
            ModLoadOrder.AutoSort(["X", "Y", "B", "A"], [Mod("A", loadBefore: ["B"]), Mod("B", loadBefore: ["X", "Y"]), Mod("X"), Mod("Y")]));

    [Fact]
    public void AutoSort_IdsNotInTheCollectionOrNotInstalled_AreDroppedOrKeptLast() =>
        Assert.Equal(
            ["B", "Missing1", "Missing2"],
            ModLoadOrder.AutoSort(["Missing1", "B", "Missing2"], [Mod("B", require: ["Other"]), Mod("Other")]));

    [Fact]
    public void Conflicts_EitherSideNamingTheOther_CountsOnceInListOrder()
    {
        var mod = Mod("A", incompatible: ["C"]);
        InstalledMod[] mods = [mod, Mod("B"), Mod("C"), Mod("D", incompatible: ["A"]), Mod("C")];

        Assert.Equal(["C", "D"], ModLoadOrder.Conflicts(mod, mods));
    }

    [Fact]
    public void Conflicts_ACopyOfTheSameMod_IsNoConflict() =>
        Assert.Empty(ModLoadOrder.Conflicts(Mod("A", incompatible: ["A"]), [Mod("A")]));
}
