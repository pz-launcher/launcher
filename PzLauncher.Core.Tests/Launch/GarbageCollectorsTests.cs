using PzLauncher.Core.Launch;

namespace PzLauncher.Core.Tests.Launch;

public class GarbageCollectorsTests
{
    public static TheoryData<GarbageCollector> AllCollectors => [.. Enum.GetValues<GarbageCollector>()];

    [Theory]
    [MemberData(nameof(AllCollectors))]
    public void Flag_EveryCollector_IsAGcFlagThatMapsBack(GarbageCollector gc)
    {
        var flag = GarbageCollectors.Flag(gc);

        Assert.True(GarbageCollectors.IsFlag(flag));
        Assert.Equal(gc, GarbageCollectors.FromFlag(flag));
    }

    [Theory]
    [InlineData("-XX:+UseZGC", true)]
    [InlineData("-XX:+UseConcMarkSweepGC", true)]
    [InlineData("-XX:+UseEpsilonGC", true)]
    [InlineData("-XX:-UseZGC", false)]
    [InlineData("-XX:+UseGC", false)]
    [InlineData("-XX:+UseStringDeduplication", false)]
    [InlineData("-XX:+UseGCOverheadLimit", false)]
    [InlineData("-XX:+UseMaximumCompactionOnSystemGC", false)]
    [InlineData("-XX:+UseAdaptiveSizePolicyWithSystemGC", false)]
    [InlineData("-XX:+usezgc", false)]
    public void IsFlag_RecognisesCollectorSelectionsOnly(string arg, bool expected) =>
        Assert.Equal(expected, GarbageCollectors.IsFlag(arg));

    [Theory]
    [InlineData("-XX:+UseEpsilonGC")]
    [InlineData(null)]
    public void FromFlag_UnknownOrNone_ReturnsNull(string? flag) => Assert.Null(GarbageCollectors.FromFlag(flag));

    [Theory]
    [InlineData(new[] { "-XX:-UseJVMCICompiler" }, true)]
    [InlineData(new[] { "-XX:-UseJVMCICompiler", "-Dx", "-XX:+UseJVMCICompiler" }, false)]
    [InlineData(new[] { "-XX:+UseJVMCICompiler", "-XX:-UseJVMCICompiler" }, true)]
    [InlineData(new string[0], false)]
    public void DisablesJvmciCompiler_LastOptionWins(string[] jvmArgs, bool expected) =>
        Assert.Equal(expected, GarbageCollectors.DisablesJvmciCompiler(jvmArgs));
}
