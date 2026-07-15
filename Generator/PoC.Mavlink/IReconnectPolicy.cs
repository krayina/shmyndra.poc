namespace Mavlink;

public interface IReconnectPolicy
{
    bool RetryInitialConnect { get; }
    TimeSpan? GetDelay(int attempt, Exception? lastError);
}

// Give up at once — e.g. a finished file replay.
public sealed class NoReconnectPolicy : IReconnectPolicy
{
    public static readonly NoReconnectPolicy Instance = new();

    private NoReconnectPolicy() { }

    public bool RetryInitialConnect => false;

    public TimeSpan? GetDelay(int attempt, Exception? lastError) => null;
}

// Fixed cadence with an optional ceiling of attempts.
public sealed class FixedReconnectPolicy : IReconnectPolicy
{
    private readonly TimeSpan _delay;
    private readonly int? _maxAttempts;

    public FixedReconnectPolicy(TimeSpan delay, int? maxAttempts = null, bool retryInitialConnect = false)
    {
        _delay = delay;
        _maxAttempts = maxAttempts;
        RetryInitialConnect = retryInitialConnect;
    }

    public bool RetryInitialConnect { get; }

    public TimeSpan? GetDelay(int attempt, Exception? lastError)
        => _maxAttempts is { } max && attempt > max ? null : _delay;
}

// Classic backoff: 0.5s → 1s → 2s → 4s … capped at 'max'.
public sealed class ExponentialBackoffPolicy : IReconnectPolicy
{
    private readonly TimeSpan _initial, _max;
    private readonly double _factor;

    public ExponentialBackoffPolicy(
        TimeSpan? initial = null,
        TimeSpan? max = null,
        double factor = 2.0,
        bool retryInitialConnect = false)
    {
        _initial = initial ?? TimeSpan.FromMilliseconds(500);
        _max = max ?? TimeSpan.FromSeconds(30);
        _factor = factor;
        RetryInitialConnect = retryInitialConnect;
    }

    public bool RetryInitialConnect { get; }

    public TimeSpan? GetDelay(int attempt, Exception? lastError)
    {
        var ticks = (long)Math.Min(
            _initial.Ticks * Math.Pow(_factor, attempt - 1), _max.Ticks);
        return TimeSpan.FromTicks(ticks);
    }
}
