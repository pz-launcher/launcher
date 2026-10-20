namespace PzLauncher.Core.Launch;

/// <param name="InstallRoot">Absolute and normalized.</param>
public sealed record GameSession(string InstallRoot, int ProcessId)
{
    /// <summary>A non-zero exit this soon after the start is a failed JVM start (<c>launch.game_exited_early</c>); later exits are the game's own.</summary>
    public static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(30);
}
