using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server;

/// <summary>
/// Named servers of one dedicated server install, run one at a time as a process that outlives the host. The running server
/// is found again from a file in <see cref="ServerSite.StateDir"/>, so every call works after a host restart.
/// </summary>
public interface IServerHostService
{
    Task<Result<IReadOnlyList<NamedServer>>> ListServersAsync(ServerSite site, CancellationToken ct = default);

    /// <summary>Writes a minimal ini with RCON settings; PZ fills in the rest on the first start.</summary>
    Task<Result<NamedServer>> CreateServerAsync(ServerSite site, string name, CancellationToken ct = default);

    Task<Result<ServerStatus>> GetStatusAsync(ServerSite site, CancellationToken ct = default);

    /// <summary>Returns once the process runs, in <see cref="ServerState.Starting"/>; <see cref="GetStatusAsync"/> reports when it is ready.</summary>
    Task<Result<ServerStatus>> StartAsync(StartServerRequest request, CancellationToken ct = default);

    /// <summary>Asks the server to save and quit, and waits for its exit.</summary>
    Task<Result<ServerStatus>> StopAsync(ServerSite site, CancellationToken ct = default);

    /// <summary>Ends the process without saving, in any state.</summary>
    Task<Result<ServerStatus>> KillAsync(ServerSite site, CancellationToken ct = default);

    /// <summary>The server's response; RCON commands don't appear in the console.</summary>
    Task<Result<string>> SendCommandAsync(ServerSite site, string command, CancellationToken ct = default);

    /// <summary>The last <paramref name="backlogLines"/> lines, then new ones, until cancelled; also while no server runs.</summary>
    IAsyncEnumerable<ServerConsoleLine> StreamConsoleAsync(ServerSite site, int backlogLines, CancellationToken ct = default);
}
