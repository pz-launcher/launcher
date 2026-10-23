using PzLauncher.Core.Common;
using PzLauncher.Core.Server.Rcon;
using PzLauncher.Core.Server;

namespace PzLauncher.Core.Tests.TestSupport;

internal sealed class FakeRconClient : IRconClient
{
    /// <summary>Whether logins succeed, like a server that has finished loading.</summary>
    public bool Accepts { get; set; } = true;

    public List<(int Port, string Password)> Logins { get; } = [];

    public List<(int Port, string Password, string Command)> Commands { get; } = [];

    /// <summary>Runs before the response is made; a test can exit the process on <c>quit</c> here.</summary>
    public Action<string>? OnCommand { get; set; }

    public Func<string, Result<string>> Respond { get; set; } = _ => "";

    public Task<Result> AuthenticateAsync(int port, string password, CancellationToken ct)
    {
        Logins.Add((port, password));
        return Task.FromResult(Accepts ? Result.Ok() : ServerErrors.RconFailed("Connection refused."));
    }

    public Task<Result<string>> ExecuteAsync(int port, string password, string command, CancellationToken ct)
    {
        Commands.Add((port, password, command));
        OnCommand?.Invoke(command);
        return Task.FromResult(Respond(command));
    }
}
