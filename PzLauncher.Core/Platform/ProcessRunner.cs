using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PzLauncher.Core.Platform;

internal sealed class ProcessRunner : IProcessRunner
{
    private const int ErrorTailLines = 50;

    // A background child of a non-interactive sh isn't a process group leader, so setsid execs in place and $! is its id.
    private const string DetachScript = "setsid \"$0\" \"$@\" </dev/null >/dev/null 2>&1 & echo $!";

    // A child that inherited the stderr pipe can keep it open after the process exits.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // stdout stays inherited: only stderr is read, for the JVM's start-up errors.
    public IRunningProcess Start(ProcessStartRequest request) => new RunningProcess(StartProcess(CreateInfo(request)));

    public IPipedProcess StartPiped(ProcessStartRequest request)
    {
        var info = CreateInfo(request);
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.StandardInputEncoding = Utf8NoBom;
        info.StandardOutputEncoding = Utf8NoBom;
        return new PipedProcess(StartProcess(info));
    }

    private static ProcessStartInfo CreateInfo(ProcessStartRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardError = true,
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        return info;
    }

    // Only a shell-executed start can return null (when it reuses a running process).
    private static Process StartProcess(ProcessStartInfo info) =>
        Process.Start(info) ?? throw new UnreachableException("Process.Start returned null without shell execute.");

    public IReadOnlyList<IRunningProcess> FindByName(string name)
    {
        using var current = Process.GetCurrentProcess();
        var found = new List<IRunningProcess>();
        foreach (var process in Process.GetProcessesByName(name))
        {
            if (InSession(process, current.SessionId))
            {
                found.Add(new FoundProcess(process));
            }
            else
            {
                process.Dispose();
            }
        }

        return found;
    }

    public DetachedProcess StartDetached(ProcessStartRequest request) =>
        OperatingSystem.IsWindows() ? WindowsDetachedStart.Start(request) : StartDetachedUnix(request);

    private DetachedProcess StartDetachedUnix(ProcessStartRequest request)
    {
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(DetachScript);
        info.ArgumentList.Add(request.FileName);
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        // sh only forks and prints the id, so these blocking calls return at once.
        using var shell = StartProcess(info);
        var output = shell.StandardOutput.ReadToEnd();
        shell.WaitForExit();
        if (!int.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            throw new IOException($"The detaching shell printed no process id: '{output.Trim()}'");
        }

        // Gone this fast means the exec itself failed: setsid's error went to /dev/null.
        return GetById(id) is { } started
            ? new DetachedProcess(id, started.StartTime)
            : throw new IOException($"{request.FileName} exited right after it was started.");
    }

    public ProcessInfo? GetById(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.HasExited ? null : new ProcessInfo(process.Id, process.ProcessName, new DateTimeOffset(process.StartTime));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Not running, exited while being read, or another user's.
            return null;
        }
    }

    public void KillTree(int id)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited since the lookup.
            }
            catch (AggregateException ex)
            {
                // A tree kill reports each process it couldn't end here, not as a Win32Exception.
                throw ex.InnerExceptions.OfType<Win32Exception>().FirstOrDefault() ?? new Win32Exception(ex.Message);
            }
        }
    }

    // Another user's processes, and ones that exited since the listing, aren't ours to wait for.
    private static bool InSession(Process process, int sessionId)
    {
        try
        {
            return process.SessionId == sessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class FoundProcess(Process process) : IRunningProcess
    {
        public int Id { get; } = process.Id;

        public int ExitCode => process.ExitCode;

        public IReadOnlyList<string> ErrorOutputTail => [];

        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);

        public void Dispose() => process.Dispose();
    }

    private sealed class PipedProcess(Process process) : IPipedProcess
    {
        private readonly RunningProcess _running = new(process);

        public int Id => _running.Id;

        public int ExitCode => _running.ExitCode;

        public IReadOnlyList<string> ErrorOutputTail => _running.ErrorOutputTail;

        public async Task SendInputAsync(string text, CancellationToken ct)
        {
            var input = process.StandardInput;
            await input.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
            await input.FlushAsync(ct).ConfigureAwait(false);
            input.Close();
        }

        public async Task<string?> ReadLineAsync(CancellationToken ct) =>
            await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);

        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Already exited, or exiting: either way it is gone.
            }
        }

        public Task WaitForExitAsync(CancellationToken ct) => _running.WaitForExitAsync(ct);

        public void Dispose() => _running.Dispose();
    }

    private sealed class RunningProcess : IRunningProcess
    {
        private readonly Process _process;
        private readonly Lock _gate = new();
        private readonly Queue<string> _errorTail = new();
        private readonly Task _drain;

        public RunningProcess(Process process)
        {
            _process = process;
            Id = process.Id;

            // Drained continuously, so a chatty game never blocks on a full pipe.
            _drain = DrainAsync(process.StandardError);
        }

        public int Id { get; }

        public int ExitCode => _process.ExitCode;

        public IReadOnlyList<string> ErrorOutputTail
        {
            get
            {
                lock (_gate)
                {
                    return [.. _errorTail];
                }
            }
        }

        public async Task WaitForExitAsync(CancellationToken ct)
        {
            await _process.WaitForExitAsync(ct).ConfigureAwait(false);
            try
            {
                await _drain.WaitAsync(DrainTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Keep what was read; the rest is lost with the pipe.
            }
        }

        public void Dispose() => _process.Dispose();

        private async Task DrainAsync(StreamReader stderr)
        {
            try
            {
                while (await stderr.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    lock (_gate)
                    {
                        _errorTail.Enqueue(line);
                        if (_errorTail.Count > ErrorTailLines)
                        {
                            _errorTail.Dequeue();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The pipe broke or the handle was released: the tail so far is all there is.
            }
        }
    }
}
