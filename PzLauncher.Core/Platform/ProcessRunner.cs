using System.Diagnostics;

namespace PzLauncher.Core.Platform;

internal sealed class ProcessRunner : IProcessRunner
{
    private const int ErrorTailLines = 50;

    // A child that inherited the stderr pipe can keep it open after the process exits.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    public IRunningProcess Start(ProcessStartRequest request)
    {
        // stdout stays inherited: only stderr is read, for the JVM's start-up errors.
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

        // Only a shell-executed start can return null (when it reuses a running process).
        var process = Process.Start(info) ?? throw new UnreachableException("Process.Start returned null without shell execute.");
        return new RunningProcess(process);
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
