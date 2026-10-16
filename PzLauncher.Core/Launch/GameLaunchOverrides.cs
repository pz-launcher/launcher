namespace PzLauncher.Core.Launch;

/// <param name="JavaHome"><c>null</c>: the install's bundled <c>jre64</c>.</param>
/// <param name="MaxMemoryMb"><c>null</c>: keep the json's <c>-Xmx</c>.</param>
/// <param name="MinMemoryMb"><c>null</c>: keep the json's <c>-Xms</c>.</param>
/// <param name="ExtraJvmArgs">Space-separated; double quotes group a value with spaces.</param>
/// <param name="ExtraGameArgs">Same syntax as <paramref name="ExtraJvmArgs"/>; passed after the main class.</param>
public sealed record GameLaunchOverrides(
    string? JavaHome,
    int? MaxMemoryMb,
    int? MinMemoryMb,
    string ExtraJvmArgs,
    string ExtraGameArgs)
{
    public static GameLaunchOverrides Empty { get; } = new(null, null, null, "", "");
}
