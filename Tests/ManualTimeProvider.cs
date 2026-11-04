// Tijd versnellen zonder echte wachttijden of externe timerpackages.
internal sealed class ManualTimeProvider(DateTimeOffset? initialUtc = null, TimeZoneInfo? zone = null) : TimeProvider
{
    private long _elapsedTicks;
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private long ElapsedTicks => Interlocked.Read(ref _elapsedTicks);
    public override DateTimeOffset GetUtcNow() => (initialUtc ?? DateTimeOffset.Parse("2026-01-01T10:00:00Z")) + TimeSpan.FromTicks(ElapsedTicks);
    public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate) _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        Interlocked.Add(ref _elapsedTicks, elapsed.Ticks);
        ManualTimer[] timers;
        lock (_gate) timers = _timers.ToArray();
        foreach (var timer in timers) timer.FireIfDue();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private readonly object _gate = new();
        private long _due;
        private TimeSpan _period;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.ElapsedTicks + dueTime.Ticks;
                _period = period;
                return true;
            }
        }

        public void FireIfDue()
        {
            lock (_gate)
            {
                if (_disposed || owner.ElapsedTicks < _due) return;
                _due = _period == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.ElapsedTicks + _period.Ticks;
            }
            callback(state);
        }

        public void Dispose()
        {
            lock (_gate) _disposed = true;
            lock (owner._gate) owner._timers.Remove(this);
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
