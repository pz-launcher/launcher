using System.Collections.Frozen;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Launch;

/// <param name="Flag">The effective <c>-XX:+Use…GC</c>; <c>null</c> when nothing selects one (the JVM's default).</param>
/// <param name="FromJson">No override: the flag comes from <c>ProjectZomboid64.json</c>.</param>
internal sealed record GcChoice(string? Flag, bool FromJson);

internal static class GarbageCollectors
{
    private const string JvmciCompilerOn = "-XX:+UseJVMCICompiler";
    private const string JvmciCompilerOff = "-XX:-UseJVMCICompiler";

    // A listed set, not any -XX:+Use<word>GC: options such as -XX:+UseMaximumCompactionOnSystemGC end in GC too.
    private static readonly FrozenSet<string> CollectorFlags =
    [
        .. Enum.GetValues<GarbageCollector>().Select(Flag),
        "-XX:+UseParallelOldGC",
        "-XX:+UseConcMarkSweepGC",
        "-XX:+UseParNewGC",
        "-XX:+UseEpsilonGC",
    ];

    public static string Flag(GarbageCollector gc) => gc switch
    {
        GarbageCollector.G1 => "-XX:+UseG1GC",
        GarbageCollector.Z => "-XX:+UseZGC",
        GarbageCollector.Shenandoah => "-XX:+UseShenandoahGC",
        GarbageCollector.Parallel => "-XX:+UseParallelGC",
        GarbageCollector.Serial => "-XX:+UseSerialGC",
    };

    public static GarbageCollector? FromFlag(string? flag)
    {
        foreach (var gc in Enum.GetValues<GarbageCollector>())
        {
            if (string.Equals(Flag(gc), flag, StringComparison.Ordinal))
            {
                return gc;
            }
        }

        return null;
    }

    /// <summary>Selects a collector, also one without a member here (CMS, Epsilon).</summary>
    public static bool IsFlag(string arg) => CollectorFlags.Contains(arg);

    /// <summary>The dropdown, else a GC typed in the extra JVM args, else the json's last GC flag.</summary>
    public static Result<GcChoice> Choose(GarbageCollector? dropdown, IReadOnlyList<string> extraJvmArgs, IReadOnlyList<string> jsonJvmArgs)
    {
        IEnumerable<string> picked = dropdown is { } gc ? [Flag(gc)] : [];
        var userFlags = picked.Concat(extraJvmArgs.Where(IsFlag)).Distinct(StringComparer.Ordinal).ToList();
        return userFlags.Count switch
        {
            0 => new GcChoice(jsonJvmArgs.LastOrDefault(IsFlag), FromJson: true),
            1 => new GcChoice(userFlags[0], FromJson: false),
            _ => LaunchErrors.GcConflict(string.Join(' ', userFlags)),
        };
    }

    /// <summary>A chosen GC replaces the json's GC flags; the dropdown's flag is added unless the extra args already hold it.</summary>
    public static IReadOnlyList<string> WithOverride(IReadOnlyList<string> jsonJvmArgs, GarbageCollector? dropdown, IReadOnlyList<string> extraJvmArgs)
    {
        var dropdownFlag = dropdown is { } gc ? Flag(gc) : null;
        if (dropdownFlag is null && !extraJvmArgs.Any(IsFlag))
        {
            return jsonJvmArgs;
        }

        // Two selected collectors make the JVM refuse to start.
        List<string> args = [.. jsonJvmArgs.Where(arg => !IsFlag(arg))];
        if (dropdownFlag is not null && !extraJvmArgs.Contains(dropdownFlag, StringComparer.Ordinal))
        {
            args.Add(dropdownFlag);
        }

        return args;
    }

    /// <summary><c>-XX:-UseJVMCICompiler</c> switches GraalVM back to C2, which supports Shenandoah; the last one wins.</summary>
    public static bool DisablesJvmciCompiler(IReadOnlyList<string> jvmArgs) =>
        jvmArgs.LastOrDefault(arg => arg is JvmciCompilerOn or JvmciCompilerOff) == JvmciCompilerOff;
}
