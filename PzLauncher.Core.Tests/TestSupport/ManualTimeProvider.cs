namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Time stands still until <see cref="Advance"/>; due one-shot timers then fire on the caller's thread.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> timers;
        lock (_lock)
        {
            _now += by;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.FireIfDue();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        public void FireIfDue()
        {
            if (_due <= owner.GetUtcNow())
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
