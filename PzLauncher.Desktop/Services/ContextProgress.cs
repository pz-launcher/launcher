using System;
using System.Threading;

namespace PzLauncher.Desktop.Services;

// Unlike Progress<T>, reports made on the creating context run inline, and without a context they never go to the thread
// pool, so they apply in order and before the result.
public sealed class ContextProgress<T>(Action<T> report) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            report(value);
        }
        else
        {
            _context.Post(_ => report(value), null);
        }
    }
}
