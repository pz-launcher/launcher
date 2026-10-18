using PzLauncher.Core.Launch;

namespace PzLauncher.Desktop.Services.Preferences;

public static class GarbageCollectorPreferences
{
    /// <summary>The value persisted in the preference file: never change one.</summary>
    public static string PreferenceName(this GarbageCollector gc) => gc switch
    {
        GarbageCollector.G1 => "g1",
        GarbageCollector.Z => "zgc",
        GarbageCollector.Shenandoah => "shenandoah",
        GarbageCollector.Parallel => "parallel",
        GarbageCollector.Serial => "serial",
    };

    public static GarbageCollector? FromPreferenceName(string? name) => PreferenceNames.Parse<GarbageCollector>(name, PreferenceName);
}
