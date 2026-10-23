using System.Threading.Channels;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessStartRequest> Requests { get; } = [];

    public List<FakeRunningProcess> Processes { get; } = [];

    /// <summary>Thrown by <see cref="Start"/> and <see cref="StartDetached"/> instead of starting a process.</summary>
    public Exception? StartException { get; set; }

    /// <summary>Like <see cref="StartException"/>, for the request it returns one for.</summary>
    public Func<ProcessStartRequest, Exception?> StartExceptionFor { get; set; } = _ => null;

    /// <summary>Runs after each start is recorded, before it can throw.</summary>
    public Action<ProcessStartRequest>? OnStart { get; set; }

    /// <summary>Processes <see cref="FindByName"/> lists by name until they exit.</summary>
    public Dictionary<string, List<FakeRunningProcess>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs before each <see cref="FindByName"/> listing; can exit processes or throw.</summary>
    public Action<string>? OnFindByName { get; set; }

    /// <summary>Processes <see cref="StartPiped"/> returned, in start order.</summary>
    public List<FakePipedProcess> PipedProcesses { get; } = [];

    /// <summary>Runs on each piped start, before it returns; scripts the process's output.</summary>
    public Action<FakePipedProcess>? OnStartPiped { get; set; }

    public ProcessStartRequest LastRequest => Assert.Single(Requests);

    /// <summary>What <see cref="GetById"/> finds; <see cref="StartDetached"/> adds to it, <see cref="KillTree"/> removes from it.</summary>
    public Dictionary<int, ProcessInfo> Alive { get; } = [];

    /// <summary>The start time <see cref="StartDetached"/> reports.</summary>
    public DateTimeOffset DetachedStartTime { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Ids <see cref="KillTree"/> was called with, in order.</summary>
    public List<int> Killed { get; } = [];

    /// <summary>Thrown by <see cref="KillTree"/>, which then leaves the process alive.</summary>
    public Exception? KillException { get; set; }

    /// <summary>Runs before each <see cref="GetById"/> lookup; can end processes.</summary>
    public Action<int>? OnGetById { get; set; }

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

    public IPipedProcess StartPiped(ProcessStartRequest request)
    {
        Requests.Add(request);
        OnStart?.Invoke(request);
        if ((StartException ?? StartExceptionFor(request)) is { } exception)
        {
            throw exception;
        }

        var process = new FakePipedProcess(3000 + PipedProcesses.Count);
        PipedProcesses.Add(process);
        OnStartPiped?.Invoke(process);
        return process;
    }

    public DetachedProcess StartDetached(ProcessStartRequest request)
    {
        Requests.Add(request);
        OnStart?.Invoke(request);
        if ((StartException ?? StartExceptionFor(request)) is { } exception)
        {
            throw exception;
        }

        var id = 4000 + Requests.Count;
        Alive[id] = new ProcessInfo(id, Path.GetFileNameWithoutExtension(request.FileName), DetachedStartTime);
        return new DetachedProcess(id, DetachedStartTime);
    }

    public ProcessInfo? GetById(int id)
    {
        OnGetById?.Invoke(id);
        return Alive.GetValueOrDefault(id);
    }

    public void KillTree(int id)
    {
        Killed.Add(id);
        if (KillException is { } exception)
        {
            throw exception;
        }

        Alive.Remove(id);
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

/// <summary>Output lines are queued by the test; <see cref="Kill"/> closes stdout and exits with -1.</summary>
internal sealed class FakePipedProcess(int id) : IPipedProcess
{
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id => id;

    public int ExitCode { get; private set; }

    public IReadOnlyList<string> ErrorOutputTail { get; set; } = [];

    /// <summary>What the caller wrote to stdin.</summary>
    public string Input { get; private set; } = "";

    public bool InputClosed { get; private set; }

    public bool Killed { get; private set; }

    public bool IsDisposed { get; private set; }

    public void Output(params string[] lines)
    {
        foreach (var line in lines)
        {
            _output.Writer.TryWrite(line);
        }
    }

    /// <summary>Closes stdout and exits, like a helper that ran to its end.</summary>
    public void Finish(int exitCode = 0)
    {
        _output.Writer.TryComplete();
        ExitCode = exitCode;
        _exited.TrySetResult();
    }

    public Task SendInputAsync(string text, CancellationToken ct)
    {
        Input += text;
        InputClosed = true;
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct) =>
        await _output.Reader.WaitToReadAsync(ct) && _output.Reader.TryRead(out var line) ? line : null;

    public void Kill()
    {
        Killed = true;
        Finish(-1);
    }

    public Task WaitForExitAsync(CancellationToken ct) => _exited.Task.WaitAsync(ct);

    public void Dispose() => IsDisposed = true;
}
