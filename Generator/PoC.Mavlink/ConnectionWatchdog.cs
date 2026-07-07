namespace Mavlink;

internal sealed class ConnectionWatchdog : IDisposable, IAsyncDisposable
{
    private readonly TimeSpan _timeout;
    private readonly Func<Task> _onTimeout;
    private readonly object _gate = new();

    private long _lastActivityTicks;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _disposed;

    public ConnectionWatchdog(TimeSpan timeout, Func<Task> onTimeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        _onTimeout = onTimeout ?? throw new ArgumentNullException(nameof(onTimeout));
    }

    public void NotifyActivity()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_task is { IsCompleted: false })
            {
                return;
            }

            NotifyActivity();
            _cts = new CancellationTokenSource();
            _task = RunAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? ctsToCancel;

        lock (_gate)
        {
            ctsToCancel = _cts;
            _cts = null;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch
            {
                // Suppress exceptions during cancellation
            }

            ctsToCancel.Dispose();
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? ctsToCancel;
        Task? taskToAwait;

        lock (_gate)
        {
            ctsToCancel = _cts;
            taskToAwait = _task;
            _cts = null;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch
            {
                // Suppress exceptions during cancellation
            }

            if (taskToAwait != null)
            {
                try
                {
                    await taskToAwait.ConfigureAwait(false);
                }
                catch
                {
                    // Suppress exceptions from the running task
                }
            }

            try
            {
                ctsToCancel.Dispose();
            }
            catch
            {
                // Suppress exceptions during disposal
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var checkInterval = TimeSpan.FromSeconds(1);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(checkInterval, ct).ConfigureAwait(false);

                var elapsed = TimeSpan.FromTicks(
                    DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastActivityTicks));

                if (elapsed > _timeout)
                {
                    Stop();

                    try
                    {
                        await _onTimeout().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Suppress exceptions from the timeout callback
                    }

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected exception when the token is canceled
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
    }
}
