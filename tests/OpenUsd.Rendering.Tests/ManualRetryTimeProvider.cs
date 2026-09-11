// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;

namespace OpenUsd.Rendering.Tests;

internal sealed class ManualRetryTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<RetryTimer> _timers = [];
    private readonly Channel<TimeSpan> _registered = Channel.CreateBounded<TimeSpan>(32);
    private long _ticks;
    private int _createdTimers;

    internal int CreatedTimers => Volatile.Read(ref _createdTimers);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Volatile.Read(ref _ticks);

    internal int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (period != Timeout.InfiniteTimeSpan || dueTime < TimeSpan.Zero)
        {
            throw new NotSupportedException("This fixture supports finite one-shot retry timers only.");
        }
        lock (_gate)
        {
            var timer = new RetryTimer(this, callback, state, checked(_ticks + dueTime.Ticks));
            _timers.Add(timer);
            Interlocked.Increment(ref _createdTimers);
            if (!_registered.Writer.TryWrite(dueTime))
            {
                throw new InvalidOperationException("The retry fixture exceeded its timer-registration bound.");
            }
            return timer;
        }
    }

    internal Task<TimeSpan> NextDelayAsync() =>
        _registered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        RetryTimer[] ready;
        lock (_gate)
        {
            _ticks = checked(_ticks + elapsed.Ticks);
            ready = _timers.Where(timer => timer.DueTicks <= _ticks).ToArray();
            foreach (RetryTimer timer in ready)
            {
                _timers.Remove(timer);
            }
        }
        foreach (RetryTimer timer in ready)
        {
            timer.Invoke();
        }
    }

    private sealed class RetryTimer(
        ManualRetryTimeProvider owner, TimerCallback callback, object? state, long dueTicks) : ITimer
    {
        private int _disposed;

        internal long DueTicks { get; } = dueTicks;

        internal void Invoke()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("A retry timer is one-shot and is never rescheduled.");

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (owner._gate)
                {
                    owner._timers.Remove(this);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
