namespace PzLauncher.Core.Platform;

/// <param name="StartTime">The OS's record of the start, as <see cref="ProcessInfo.StartTime"/> reports it later.</param>
internal sealed record DetachedProcess(int Id, DateTimeOffset StartTime);
