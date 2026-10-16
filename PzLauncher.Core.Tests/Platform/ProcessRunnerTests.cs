using PzLauncher.Core.Platform;

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
}
