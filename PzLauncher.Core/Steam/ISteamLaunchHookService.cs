using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam;

/// <summary>Makes Steam's Play button for Project Zomboid start the launcher, for the current Steam account; Windows only.</summary>
public interface ISteamLaunchHookService
{
    /// <summary>Reads the launch options Steam has saved now; <paramref name="launcherExe"/> is the running launcher's absolute path.</summary>
    Task<Result<SteamLaunchHookStatus>> GetStatusAsync(string launcherExe, CancellationToken ct = default);

    /// <summary>
    /// Adds or removes the hook, keeping the rest of the launch options. Steam saves its config on exit, so a running Steam
    /// is <c>steam.client_running</c> unless <paramref name="restartSteam"/>: then it is shut down first and started again.
    /// </summary>
    Task<Result<SteamLaunchHookApplied>> SetAsync(string launcherExe, bool enabled, bool restartSteam, CancellationToken ct = default);
}
