using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server;

/// <param name="Name"><c>null</c> when stopped, like <paramref name="ProcessId"/> and <paramref name="StartedAt"/>.</param>
/// <param name="LastFailure">Why the server last stopped on its own; kept until the next start, but not across host restarts.</param>
public sealed record ServerStatus(ServerState State, string? Name, int? ProcessId, DateTimeOffset? StartedAt, Error? LastFailure);
