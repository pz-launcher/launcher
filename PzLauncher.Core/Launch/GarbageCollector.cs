namespace PzLauncher.Core.Launch;

/// <summary>The settings page offers G1, Z and Shenandoah; Parallel and Serial only describe a json.</summary>
public enum GarbageCollector
{
    G1,
    Z,
    Shenandoah,
    Parallel,
    Serial,
}
