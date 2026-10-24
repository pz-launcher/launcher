namespace PzLauncher.Core.Platform.Processes;

/// <param name="Name">The executable name without extension.</param>
internal sealed record ProcessInfo(int Id, string Name, DateTimeOffset StartTime);
