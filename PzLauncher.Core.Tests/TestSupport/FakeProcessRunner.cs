using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessStartRequest> Requests { get; } = [];

    public List<FakeRunningProcess> Processes { get; } = [];

    /// <summary>Thrown by <see cref="Start"/> instead of starting a process.</summary>
    public Exception? StartException { get; set; }

    /// <summary>Like <see cref="StartException"/>, for the request it returns one for.</summary>
    public Func<ProcessStartRequest, Exception?> StartExceptionFor { get; set; } = _ => null;

    /// <summary>Runs after each start is recorded, before it can throw.</summary>
    public Action<ProcessStartRequest>? OnStart { get; set; }

    /// <summary>Processes <see cref="FindByName"/> lists by name until they exit.</summary>
    public Dictionary<string, List<FakeRunningProcess>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs before each <see cref="FindByName"/> listing; can exit processes or throw.</summary>
    public Action<string>? OnFindByName { get; set; }

    public ProcessStartRequest LastRequest => Assert.Single(Requests);

    public FakeRunningProcess AddRunning(string name)
    {
        var process = new FakeRunningProcess(2000 + Running.Values.Sum(list => list.Count));
        if (!Running.TryGetValue(name, out var list))
        {
            list = [];
            Running[name] = list;
        }

        list.Add(process);
        return process;
    }

    public IReadOnlyList<IRunningProcess> FindByName(string name)
    {
        OnFindByName?.Invoke(name);
        return Running.TryGetValue(name, out var list) ? [.. list.Where(process => !process.HasExited)] : [];
    }

    public IRunningProcess Start(ProcessStartRequest request)
    {
        Requests.Add(request);
        OnStart?.Invoke(request);
        if ((StartException ?? StartExceptionFor(request)) is { } exception)
        {
            throw exception;
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

    public bool HasExited => _exited.Task.IsCompleted;

    public void Exit(int exitCode = 0)
    {
        ExitCode = exitCode;
        _exited.TrySetResult();
    }

    public Task WaitForExitAsync(CancellationToken ct) => _exited.Task.WaitAsync(ct);

    public void Dispose() => IsDisposed = true;
}
