using PzLauncher.Core.Common;
using PzLauncher.Core.Java;

namespace PzLauncher.Core.Launch;

/// <param name="MaxMemoryMb">The json's effective <c>-Xmx</c>; <c>null</c> when absent or unparseable.</param>
/// <param name="MinMemoryMb">The json's effective <c>-Xms</c>; <c>null</c> when absent or unparseable.</param>
/// <param name="VmArgs">The json's JVM args, including the entry for this Windows version.</param>
/// <param name="BundledJava">The install's own <c>jre64</c>; <c>null</c> when it isn't a usable Java.</param>
/// <param name="Warnings">A <c>ProjectZomboid64.json</c> that can't be read; the json values are then empty.</param>
/// <param name="GarbageCollector">The json's effective GC flag; <c>null</c> when it sets none, or one without a member.</param>
/// <param name="GarbageCollectorSetByViewpoint">It is Shenandoah and the Viewpoint mod's json backup exists.</param>
public sealed record GameLaunchDefaults(
    int? MaxMemoryMb,
    int? MinMemoryMb,
    IReadOnlyList<string> VmArgs,
    JavaRuntime? BundledJava,
    int SystemMemoryMb,
    IReadOnlyList<Error> Warnings,
    GarbageCollector? GarbageCollector,
    bool GarbageCollectorSetByViewpoint);
