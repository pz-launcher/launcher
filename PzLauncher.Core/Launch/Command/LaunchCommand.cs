using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch.Config;
using PzLauncher.Core.Platform.Processes;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch.Command;

/// <summary>The loader variables a Linux java process of this install needs; the game and the server differ.</summary>
internal delegate Dictionary<string, string> LinuxEnvironmentBuilder(
    string gameFolder, string javaHome, string? inheritedLibraryPath, string? inheritedPreload);

/// <summary>What the game and the dedicated server share to start java from an install's <c>ProjectZomboid64.json</c>.</summary>
internal sealed record LaunchCommand(string GameFolder, PzLaunchConfig Config)
{
    /// <summary><paramref name="gameFolder"/> as <see cref="PzLaunchFiles.ResolveGameFolder"/> finds it.</summary>
    public static async Task<Result<LaunchCommand>> ReadAsync(string gameFolder, CancellationToken ct)
    {
        var config = await PzLaunchConfig.ReadAsync(Path.Combine(gameFolder, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return config.Error;
        }

        return new LaunchCommand(gameFolder, config.Value);
    }

    public static string BundledJavaHome(string gameFolder) => Path.Combine(gameFolder, PzLaunchFiles.BundledJreFolder);

    /// <summary><paramref name="executableName"/> picks the windowed or the console java (<see cref="JavaLocations"/>).</summary>
    public static Result<string> FindJava(string javaHome, string executableName)
    {
        var path = Path.Combine(javaHome, JavaLocations.BinFolder, executableName);
        if (!File.Exists(path))
        {
            return LaunchErrors.JavaMissing(path);
        }

        return path;
    }

    public IReadOnlyList<string> JsonJvmArgs(IPlatformEnvironment platform) => Config.JvmArgs(platform.Os, platform.OsVersion);

    /// <summary><paramref name="appArgs"/> follow the main class and the overrides' extra game args.</summary>
    public ProcessStartRequest Start(
        IPlatformEnvironment platform,
        string java,
        string javaHome,
        IReadOnlyList<string> jvmArgs,
        GameLaunchOverrides overrides,
        IReadOnlyList<string> appArgs,
        LinuxEnvironmentBuilder linuxEnvironment) =>
        new(
            java,
            [.. LaunchArguments.Build(Config, platform.Os, jvmArgs, overrides), .. appArgs],
            GameFolder,
            BuildEnvironment(platform, linuxEnvironment, javaHome));

    private Dictionary<string, string> BuildEnvironment(IPlatformEnvironment platform, LinuxEnvironmentBuilder linux, string javaHome) => platform.Os switch
    {
        PlatformKind.Linux => linux(
            GameFolder,
            javaHome,
            platform.GetEnvironmentVariable(LinuxLaunchEnvironment.LibraryPathVariable),
            platform.GetEnvironmentVariable(LinuxLaunchEnvironment.PreloadVariable)),
        PlatformKind.Windows or PlatformKind.MacOS => [],
    };
}
