using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server;

/// <summary>Source RCON to a server on this machine; one connection per call. Failures are <c>server.rcon_failed</c>.</summary>
internal interface IRconClient
{
    /// <summary>Connects and logs in only: whether the server is ready for commands.</summary>
    Task<Result> AuthenticateAsync(int port, string password, CancellationToken ct);

    /// <summary>The whole response, however many packets it took.</summary>
    Task<Result<string>> ExecuteAsync(int port, string password, string command, CancellationToken ct);
}
