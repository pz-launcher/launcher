using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Platform;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Start_ChildWritesStderrAndFails_KeepsTheLast50LinesAndTheExitCode()
    {
        var request = OperatingSystem.IsWindows()
            ? new ProcessStartRequest("cmd.exe", ["/c", "(for /L %i in (1,1,60) do @(1>&2 echo line%i)) & exit /b 3"], Path.GetTempPath(), new Dictionary<string, string>())
            : new ProcessStartRequest(
                "/bin/sh", ["-c", "i=1; while [ $i -le 60 ]; do echo line$i >&2; i=$((i+1)); done; exit 3"], Path.GetTempPath(), new Dictionary<string, string>());

        using var process = new ProcessRunner().Start(request);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, process.ExitCode);
        Assert.Equal(Enumerable.Range(11, 50).Select(i => $"line{i}"), process.ErrorOutputTail);
    }

    [Fact]
    public void FindByName_ThisProcessName_IncludesThisProcess()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();

        var found = new ProcessRunner().FindByName(current.ProcessName);

        try
        {
            Assert.Contains(found, process => process.Id == Environment.ProcessId);
        }
        finally
        {
            foreach (var process in found)
            {
                process.Dispose();
            }
        }
    }

    [Fact]
    public void FindByName_NoSuchProcess_IsEmpty() =>
        Assert.Empty(new ProcessRunner().FindByName("pzlauncher-no-such-process-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void GetById_ThisProcess_ReturnsItsNameAndStartTime()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();

        var found = new ProcessRunner().GetById(Environment.ProcessId);

        Assert.Equal(new ProcessInfo(Environment.ProcessId, current.ProcessName, new DateTimeOffset(current.StartTime)), found);
    }

    [Fact]
    public void GetById_NoSuchProcess_IsNull() => Assert.Null(new ProcessRunner().GetById(int.MaxValue));

    [Fact]
    public void KillTree_NoSuchProcess_DoesNothing() => new ProcessRunner().KillTree(int.MaxValue);

    [Fact]
    public async Task StartDetached_Child_GetsEofOnStdinAndTheEnvironmentUntilKilled()
    {
        using var temp = new TempDirectory();
        var marker = Path.Combine(temp.Path, "marker");
        var environment = new Dictionary<string, string> { ["PZLAUNCHER_TEST"] = "eof" };

        // Reads stdin to its end, writes the variable, then lingers until killed. Windows uses cmd built-ins only: a console
        // program started without a console would open a window.
        var request = OperatingSystem.IsWindows()
            ? new ProcessStartRequest(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "set /p line= & echo %PZLAUNCHER_TEST%> marker & for /L %i in (0,0,1) do @rem"],
                temp.Path,
                environment)
            : new ProcessStartRequest("/bin/sh", ["-c", "read line; echo \"$PZLAUNCHER_TEST\" > marker; sleep 60"], temp.Path, environment);
        var runner = new ProcessRunner();

        var started = runner.StartDetached(request);
        try
        {
            await WaitUntilAsync(() => ReadMarker(marker) == "eof");
            var found = runner.GetById(started.Id);

            Assert.NotNull(found);
            Assert.InRange((found.StartTime - started.StartTime).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        finally
        {
            runner.KillTree(started.Id);
        }

        await WaitUntilAsync(() => runner.GetById(started.Id) is null);
    }

    private static string? ReadMarker(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            // Still being written.
            return null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), "Timed out waiting for the child process.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
