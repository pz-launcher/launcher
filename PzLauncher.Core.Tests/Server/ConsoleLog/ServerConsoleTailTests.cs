using PzLauncher.Core.Server.ConsoleLog;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server.ConsoleLog;

public sealed class ServerConsoleTailTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _path;

    public ServerConsoleTailTests() => _path = Path.Combine(_temp.Path, "server-console.txt");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Write(string text) => File.WriteAllText(_path, text);

    private void Append(string text) => File.AppendAllText(_path, text);

    private static IEnumerable<string> Texts(IEnumerable<ServerConsoleLine> lines) => lines.Select(line => line.Text);

    [Fact]
    public async Task ReadBacklogAsync_ReturnsTheLastCompleteLinesWithoutLineEnds()
    {
        Write("one\r\ntwo\nthree\r\npartial");

        Assert.Equal(["two", "three"], await new ServerConsoleTail(_path).ReadBacklogAsync(2, Ct));
    }

    [Fact]
    public async Task ReadBacklogAsync_RealFailedStart_EndsWithTheShutdown()
    {
        File.Copy(Fixtures.Path("Server", "console-admin-prompt.txt"), _path);

        var lines = await new ServerConsoleTail(_path).ReadBacklogAsync(20, Ct);

        Assert.Equal(20, lines.Count);
        Assert.Contains(lines, line => line.Contains("Enter new administrator password", StringComparison.Ordinal));
        Assert.EndsWith("at zombie.network.GameServer$1.run(GameServer.java:380)", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadBacklogAsync_MissingFile_IsEmpty() =>
        Assert.Empty(await new ServerConsoleTail(_path).ReadBacklogAsync(10, Ct));

    [Fact]
    public async Task ReadBacklogAsync_FileLongerThanTheWindow_StartsAtAWholeLine()
    {
        Write(string.Concat(Enumerable.Range(0, 30_000).Select(i => $"line{i}\n")));

        var lines = await new ServerConsoleTail(_path).ReadBacklogAsync(int.MaxValue, Ct);

        Assert.InRange(lines.Count, 20_000, 29_999);
        Assert.Equal(Enumerable.Range(30_000 - lines.Count, lines.Count).Select(i => $"line{i}"), lines);
    }

    [Fact]
    public async Task ReadNewAsync_AfterBacklog_ReturnsOnlyNewCompleteLines()
    {
        Write("old\npart");
        var tail = new ServerConsoleTail(_path);
        await tail.ReadBacklogAsync(10, Ct);

        Append("ial\nnext");
        var first = await tail.ReadNewAsync(Ct);
        Append("\n");
        var second = await tail.ReadNewAsync(Ct);

        Assert.Equal(["partial"], Texts(first));
        Assert.Equal(["next"], Texts(second));
        Assert.Empty(await tail.ReadNewAsync(Ct));
    }

    [Fact]
    public async Task ReadNewAsync_FileShrank_ReportsTheRestartAndReadsFromTheStart()
    {
        Write("first run 1\nfirst run 2\n");
        var tail = new ServerConsoleTail(_path);
        await tail.ReadBacklogAsync(10, Ct);

        Write("second\n");
        var lines = await tail.ReadNewAsync(Ct);

        Assert.Equal(
            [new ServerConsoleLine("", ServerConsoleLineKind.FileRestarted), new ServerConsoleLine("second", ServerConsoleLineKind.Output)],
            lines);
    }

    [Fact]
    public async Task ReadNewAsync_FileDeleted_ReportsTheRestartOnce()
    {
        Write("line\n");
        var tail = new ServerConsoleTail(_path);
        await tail.ReadBacklogAsync(10, Ct);

        File.Delete(_path);

        Assert.Equal([ServerConsoleLineKind.FileRestarted], (await tail.ReadNewAsync(Ct)).Select(line => line.Kind));
        Assert.Empty(await tail.ReadNewAsync(Ct));
    }

    [Fact]
    public async Task ReadNewAsync_InvalidUtf8_IsReplaced()
    {
        var tail = new ServerConsoleTail(_path);
        await tail.ReadBacklogAsync(10, Ct);

        await File.WriteAllBytesAsync(_path, [.. "a"u8, 0xFF, .. "b\n"u8], Ct);

        Assert.Equal(["a�b"], Texts(await tail.ReadNewAsync(Ct)));
    }

    [Fact]
    public async Task FollowAsync_EmitsTheBacklogThenNewLines()
    {
        Write("one\ntwo\nthree\n");
        await using var lines = ServerConsoleTail.FollowAsync(_path, 2, new SteppingTimeProvider(), Ct).GetAsyncEnumerator(Ct);

        Assert.True(await lines.MoveNextAsync());
        Assert.Equal("two", lines.Current.Text);
        Assert.True(await lines.MoveNextAsync());
        Assert.Equal("three", lines.Current.Text);

        Append("four\n");
        Assert.True(await lines.MoveNextAsync());
        Assert.Equal(new ServerConsoleLine("four", ServerConsoleLineKind.Output), lines.Current);
    }

    [Fact]
    public async Task FollowAsync_Cancelled_Throws()
    {
        Write("one\n");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var lines = ServerConsoleTail.FollowAsync(_path, 10, new ManualTimeProvider(), cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await lines.MoveNextAsync());

        var next = lines.MoveNextAsync();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await next);
    }
}
