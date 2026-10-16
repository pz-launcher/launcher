using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessStartRequest> Requests { get; } = [];

    public List<FakeRunningProcess> Processes { get; } = [];

    /// <summary>Thrown by <see cref="Start"/> instead of starting a process.</summary>
    public Exception? StartException { get; set; }

    public ProcessStartRequest LastRequest => Assert.Single(Requests);

    public IRunningProcess Start(ProcessStartRequest request)
    {
        Requests.Add(request);
        if (StartException is not null)
        {
            throw StartException;
        }

        var process = new FakeRunningProcess(1000 + Processes.Count);
        Processes.Add(process);
        return process;
    }
}

internal sealed class FakeRunningProcess(int id) : IRunningProcess
{
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id => id;

    public int ExitCode { get; private set; }

    public IReadOnlyList<string> ErrorOutputTail { get; set; } = [];

    public bool IsDisposed { get; private set; }

    public void Exit(int exitCode = 0)
    {
        ExitCode = exitCode;
        _exited.TrySetResult();
    }

    public Task WaitForExitAsync(CancellationToken ct) => _exited.Task.WaitAsync(ct);

    public void Dispose() => IsDisposed = true;
}
