using System.Diagnostics;

namespace PzLauncher.Core.Platform;

internal sealed class ProcessRunner : IProcessRunner
{
    public IRunningProcess Start(ProcessStartRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory,
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        // Only a shell-executed start can return null (when it reuses a running process).
        var process = Process.Start(info) ?? throw new UnreachableException("Process.Start returned null without shell execute.");
        return new RunningProcess(process);
    }

    private sealed class RunningProcess(Process process) : IRunningProcess
    {
        public int Id { get; } = process.Id;

        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);

        public void Dispose() => process.Dispose();
    }
}
