namespace PzLauncher.Core.Tests.TestSupport.Fakes;

/// <summary>Every timer fires at once and moves the clock by its due time, so timed waits and polls take no real time.</summary>
public sealed class SteppingTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _elapsedTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);

    public override DateTimeOffset GetUtcNow() => Start.AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Interlocked.Add(ref _elapsedTicks, dueTime.Ticks);

            // Queued: a callback run inside CreateTimer would complete the caller's timer before it holds it.
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new FiredTimer();
    }

    private sealed class FiredTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
