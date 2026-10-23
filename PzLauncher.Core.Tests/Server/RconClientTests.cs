using System.Net;
using System.Net.Sockets;
using PzLauncher.Core.Common;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server;

public sealed class RconClientTests
{
    private const string Password = "secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RconClient Client(TimeProvider? time = null) => new(time ?? new ManualTimeProvider());

    [Fact]
    public async Task ExecuteAsync_ShortResponse_ReturnsIt()
    {
        await using var server = new FakeRconServer(Password, _ => "Players connected (0): \n");

        var response = ResultAssert.Ok(await Client().ExecuteAsync(server.Port, Password, "players", Ct));

        Assert.Equal("Players connected (0): \n", response);
        Assert.Equal(["players"], server.Commands);
    }

    [Fact]
    public async Task ExecuteAsync_ResponseOverSeveralPackets_JoinsThemWhole()
    {
        // Multi-byte characters: the 4086-byte packets cut some of them in two.
        var help = string.Concat(Enumerable.Repeat("命令-", 2000));
        await using var server = new FakeRconServer(Password, _ => help);

        Assert.Equal(help, ResultAssert.Ok(await Client().ExecuteAsync(server.Port, Password, "help", Ct)));
    }

    [Fact]
    public async Task AuthenticateAsync_RightPassword_SucceedsWithoutACommand()
    {
        await using var server = new FakeRconServer(Password, _ => "");

        ResultAssert.Ok(await Client().AuthenticateAsync(server.Port, Password, Ct));

        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task ExecuteAsync_WrongPassword_FailsWithoutSending()
    {
        await using var server = new FakeRconServer(Password, _ => "");

        ResultAssert.Failed(await Client().ExecuteAsync(server.Port, "wrong", "players", Ct), ErrorCode.ServerRconFailed);
        ResultAssert.Failed(await Client().AuthenticateAsync(server.Port, "wrong", Ct), ErrorCode.ServerRconFailed);
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task ExecuteAsync_ServerClosesMidResponse_Fails()
    {
        await using var server = new FakeRconServer(Password, _ => new string('x', 10_000)) { CloseMidResponse = true };

        ResultAssert.Failed(await Client().ExecuteAsync(server.Port, Password, "help", Ct), ErrorCode.ServerRconFailed);
    }

    [Fact]
    public async Task ExecuteAsync_NothingListening_Fails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        ResultAssert.Failed(await Client().ExecuteAsync(port, Password, "players", Ct), ErrorCode.ServerRconFailed);
    }

    [Fact]
    public async Task ExecuteAsync_NoAnswer_FailsAfterTheTimeout()
    {
        await using var server = new FakeRconServer(Password, _ => "") { Silent = true };

        var error = ResultAssert.Failed(
            await Client(new SteppingTimeProvider()).ExecuteAsync(server.Port, Password, "players", Ct), ErrorCode.ServerRconFailed);

        Assert.Contains("30 seconds", error.Details["reason"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_Throws()
    {
        await using var server = new FakeRconServer(Password, _ => "") { Silent = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var call = Client().ExecuteAsync(server.Port, Password, "players", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }
}
