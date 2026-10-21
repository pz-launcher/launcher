using PzLauncher.Core.Common;

namespace PzLauncher.Core.Launch;

/// <summary>Starts the game client with a launcher-built java command; tracks at most one game per launcher session.</summary>
public interface IGameLaunchService
{
    Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default);

    Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default);

    /// <summary><see cref="LaunchAsync"/>'s checks without starting anything; never <c>launch.already_running</c>.</summary>
    Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default);

    /// <summary>Completes when the tracked game exits (a failure when its JVM crashed or failed to start), immediately when none runs.</summary>
    Task<Result> WaitForExitAsync(CancellationToken ct = default);

    /// <summary><c>true</c> once the game started last has outlived the window in which an exit counts as a failed start; <c>false</c> when it exited within it or none was started. Stays decided after that game exits.</summary>
    Task<bool> WaitPastEarlyExitAsync(CancellationToken ct = default);
}
