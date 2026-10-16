namespace PzLauncher.Core.Launch;

public sealed record LaunchGameRequest(string InstallRoot, GameLaunchOverrides Overrides);
