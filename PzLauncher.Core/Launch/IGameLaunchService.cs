using PzLauncher.Core.Common;

namespace PzLauncher.Core.Launch;

/// <summary>Starts the game client with a launcher-built java command; tracks at most one game per launcher session.</summary>
public interface IGameLaunchService
{
    Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default);

    Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default);

    /// <summary>Completes when the tracked game exits, immediately when none runs.</summary>
    Task WaitForExitAsync(CancellationToken ct = default);
}
