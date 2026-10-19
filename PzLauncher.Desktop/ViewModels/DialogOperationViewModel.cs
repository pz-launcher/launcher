using System;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels;

/// <summary>A dialog that runs an operation: the dialog starts it when it opens, and closing the dialog cancels a run in progress.</summary>
public abstract class DialogOperationViewModel : DialogViewModel
{
    private readonly CancellationTokenSource _cancel = new();

    protected bool IsStarted { get; private set; }

    protected CancellationToken AbortToken => _cancel.Token;

    /// <summary>Runs once; later calls return at once.</summary>
    public Task StartAsync()
    {
        if (IsStarted)
        {
            return Task.CompletedTask;
        }

        IsStarted = true;
        return RunAsync();
    }

    /// <summary>Cancels a run in progress; for a dialog closed by the window's own close button.</summary>
    public void Abort() => _cancel.Cancel();

    protected abstract Task RunAsync();

    protected void AbortAndClose()
    {
        Abort();
        RequestClose(false);
    }

    /// <summary>Runs the operation with its progress reports applied before its result; <c>null</c> when aborted.</summary>
    protected async Task<TResult?> RunWithProgressAsync<TReport, TResult>(
        Func<IProgress<TReport>, CancellationToken, Task<TResult>> operation, Action<TReport> onReport)
        where TResult : struct
    {
        var ct = _cancel.Token;

        // Reports posted before the result arrives are applied before it; anything later is ignored.
        var finished = false;
        var progress = new ContextProgress<TReport>(report =>
        {
            if (!finished)
            {
                onReport(report);
            }
        });

        try
        {
            return await operation(progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The dialog is closing.
            return null;
        }
        finally
        {
            finished = true;
        }
    }
}
