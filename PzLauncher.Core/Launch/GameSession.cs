namespace PzLauncher.Core.Launch;

/// <param name="InstallRoot">Absolute and normalized.</param>
public sealed record GameSession(string InstallRoot, int ProcessId);
