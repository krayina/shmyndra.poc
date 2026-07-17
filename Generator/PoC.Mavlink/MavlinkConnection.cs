using System.Net.Sockets;

namespace Mavlink;

internal sealed class MavlinkConnection : IMavlinkConnection, IDisposable, IAsyncDisposable
{
    private readonly IMavlinkPortProvider _provider;
    private readonly IReconnectPolicy _policy;
    private readonly bool _pumpToPipe;
    private readonly System.IO.Pipelines.Pipe _pipe;

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _stateLock = new();

    private TaskCompletionSource<bool> _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile IMavlinkPort? _port;
    private CancellationTokenSource? _life;
    private Task? _lifeTask;
    private Exception? _lastError;
    private MavlinkConnectionState _state = MavlinkConnectionState.Disconnected;
    private int _disposed;

    public MavlinkConnection(IMavlinkPortProvider provider, IReconnectPolicy policy, bool pumpToPipe = true)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _pumpToPipe = pumpToPipe;
        _pipe = new System.IO.Pipelines.Pipe(System.IO.Pipelines.PipeOptions.Default);
    }

    public System.IO.Pipelines.PipeReader Input => _pipe.Reader;

    public MavlinkConnectionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged;

    public void Abort()
    {
        _ = ClosePortAsync();
    }

    private void SetState(MavlinkConnectionState value, Exception? error = null)
    {
        MavlinkConnectionStateChangedEventArgs? args = null;

        lock (_stateLock)
        {
            if (_state == value)
            {
                return;
            }

            args = new MavlinkConnectionStateChangedEventArgs
            {
                OldState = _state,
                NewState = value,
                Error = error
            };

            _state = value;

            switch (value)
            {
                case MavlinkConnectionState.Connected:
                    _connectedTcs.TrySetResult(true);
                    break;

                case MavlinkConnectionState.Disconnected:
                case MavlinkConnectionState.ConnectionLost:
                    _connectedTcs.TrySetException(new MavlinkConnectionException($"Connection lost: {value}", error));
                    _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    break;

                case MavlinkConnectionState.Reconnecting:
                case MavlinkConnectionState.Connecting:
                    if (_connectedTcs.Task.IsCompleted)
                    {
                        _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    break;
            }
        }

        if (args != null)
        {
            try
            {
                StateChanged?.Invoke(args);
            }
            catch
            {
                // Suppress listener faults
            }
        }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            MavlinkConnectionState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }

            if (currentState is not (MavlinkConnectionState.Disconnected or MavlinkConnectionState.ConnectionLost))
            {
                return;
            }

            _life = new CancellationTokenSource();
            SetState(MavlinkConnectionState.Connecting);
            _lifeTask = Task.Run(() => LifecycleLoopAsync(_life.Token), CancellationToken.None);
        }
        finally
        {
            _connectGate.Release();
        }

        TaskCompletionSource<bool> tcs;
        lock (_stateLock)
        {
            tcs = _connectedTcs;
        }

#if NET6_0_OR_GREATER
        await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
#else
        var tcsTask = tcs.Task;
        if (!tcsTask.IsCompleted)
        {
            var delayTcs = new TaskCompletionSource<bool>();
            using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), delayTcs))
            {
                if (await Task.WhenAny(tcsTask, delayTcs.Task).ConfigureAwait(false) == delayTcs.Task)
                {
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
        await tcsTask.ConfigureAwait(false);
#endif
    }

    private async Task LifecycleLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await OpenPortAsync(ct).ConfigureAwait(false);
                    SetState(MavlinkConnectionState.Connected);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _lastError = ex;
                    await ClosePortAsync().ConfigureAwait(false);

                    if (!_policy.RetryInitialConnect || !_provider.CanRecreatePort)
                    {
                        SetState(MavlinkConnectionState.ConnectionLost, ex);
                        return;
                    }

                    SetState(MavlinkConnectionState.Reconnecting, ex);

                    if (!await WaitBeforeRetryAsync(1, ct).ConfigureAwait(false))
                    {
                        SetState(MavlinkConnectionState.ConnectionLost, _lastError);
                        return;
                    }
                    continue;
                }

                try
                {
                    await PumpAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _lastError = ex;
                }

                await ClosePortAsync().ConfigureAwait(false);

                if (!_provider.CanRecreatePort || ct.IsCancellationRequested)
                {
                    SetState(MavlinkConnectionState.ConnectionLost, _lastError);
                    return;
                }

                SetState(MavlinkConnectionState.Reconnecting, _lastError);
                int attempt = 1;
                bool reconnected = false;

                while (!ct.IsCancellationRequested)
                {
                    if (!await WaitBeforeRetryAsync(attempt++, ct).ConfigureAwait(false))
                    {
                        break;
                    }

                    try
                    {
                        await OpenPortAsync(ct).ConfigureAwait(false);
                        reconnected = true;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex;
                    }
                }

                if (!reconnected)
                {
                    SetState(MavlinkConnectionState.ConnectionLost, _lastError);
                    return;
                }

                SetState(MavlinkConnectionState.Connected);
            }
        }
        finally
        {
        }
    }

    private async Task<bool> WaitBeforeRetryAsync(int attempt, CancellationToken ct)
    {
        var delay = _policy.GetDelay(attempt, _lastError);
        if (!delay.HasValue)
        {
            return false;
        }

        if (delay.Value > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay.Value, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return !ct.IsCancellationRequested;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            TaskCompletionSource<bool> tcs;
            MavlinkConnectionState currentState;

            lock (_stateLock)
            {
                currentState = _state;
                tcs = _connectedTcs;
            }

            if (currentState == MavlinkConnectionState.Connected && _port != null)
            {
                var port = _port;
                if (port == null)
                {
                    continue;
                }

                await _writeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_state != MavlinkConnectionState.Connected || _port == null)
                    {
                        throw new MavlinkConnectionException("Connection lost while waiting to write.");
                    }

                    try
                    {
                        await port.WriteAsync(data, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or SocketException or IOException)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ct);
                        }
                        throw new MavlinkConnectionException("Port error during write.", ex);
                    }
                }
                finally
                {
                    _writeGate.Release();
                }

                return;
            }

            if (currentState is MavlinkConnectionState.Disconnected
                or MavlinkConnectionState.ConnectionLost
                or MavlinkConnectionState.Disconnecting)
            {
                throw new MavlinkConnectionException($"Cannot write in state: {currentState}.", _lastError);
            }

#if NET6_0_OR_GREATER
            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
#else
            var tcsTask = tcs.Task;
            if (!tcsTask.IsCompleted)
            {
                var delayTcs = new TaskCompletionSource<bool>();
                using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), delayTcs))
                {
                    if (await Task.WhenAny(tcsTask, delayTcs.Task).ConfigureAwait(false) == delayTcs.Task)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                }
            }
            await tcsTask.ConfigureAwait(false);
#endif
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("Port is null.");
        if (!_pumpToPipe)
        {
            await DrainAndDiscardAsync(port, ct).ConfigureAwait(false);
            return;
        }

        var writer = _pipe.Writer;

#if NETSTANDARD2_1_OR_GREATER
        if (port.Reader is { } src)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var res = await src.ReadAsync(ct).ConfigureAwait(false);
                    var buf = res.Buffer;

                    foreach (var seg in buf)
                    {
                        var mem = writer.GetMemory(seg.Length);
                        seg.Span.CopyTo(mem.Span);
                        writer.Advance(seg.Length);
                    }

                    src.AdvanceTo(buf.End);
                    var flush = await writer.FlushAsync(ct).ConfigureAwait(false);

                    if (flush.IsCompleted || res.IsCompleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                try
                {
                    await writer.CompleteAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Suppress exceptions during writer completion
                }
            }
            return;
        }
#endif

        var pool = System.Buffers.ArrayPool<byte>.Shared;
        var bufArray = pool.Rent(4096);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await port.ReadAsync(bufArray.AsMemory(0, bufArray.Length), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                var mem = writer.GetMemory(n);
                bufArray.AsSpan(0, n).CopyTo(mem.Span);
                writer.Advance(n);

                var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (flush.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            pool.Return(bufArray);

            try
            {
                await writer.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // Suppress exceptions during writer completion
            }
        }
    }

    private static async Task DrainAndDiscardAsync(IMavlinkPort port, CancellationToken ct)
    {
        var pool = System.Buffers.ArrayPool<byte>.Shared;
        var buf = pool.Rent(4096);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await port.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break; // EOF → lifecycle loop: reconnect or ConnectionLost
                }
            }
        }
        finally
        {
            pool.Return(buf);
        }
    }

    private async Task OpenPortAsync(CancellationToken ct)
    {
        _port = await _provider.CreatePortAsync(ct).ConfigureAwait(false);
        _lastError = null;
    }

    private async Task ClosePortAsync()
    {
        var port = _port;
        _port = null;

        if (port == null)
        {
            return;
        }

        try
        {
            if (port is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                port.Dispose();
            }
        }
        catch
        {
            // Best effort disposal
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? life;
        Task? lifeTask;

        lock (_stateLock)
        {
            if (_state == MavlinkConnectionState.Disconnected)
            {
                return;
            }

            life = _life;
            lifeTask = _lifeTask;
        }

        if (life == null)
        {
            return;
        }

        SetState(MavlinkConnectionState.Disconnecting);
        life.Cancel();

        if (lifeTask != null)
        {
            try
            {
                await lifeTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await ClosePortAsync().ConfigureAwait(false);

        _life?.Dispose();
        _life = null;
        _lifeTask = null;

        SetState(MavlinkConnectionState.Disconnected);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkConnection));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _life?.Cancel();
        }
        catch
        {
            // Suppress cancellation exceptions
        }

        var port = _port;
        _port = null;
        port?.Dispose();

        _pipe.Writer.Complete();

        _connectGate.Dispose();
        _writeGate.Dispose();

        try
        {
            _life?.Dispose();
        }
        catch
        {
            // Suppress disposal exceptions
        }

        try
        {
            (_provider as IDisposable)?.Dispose();
        }
        catch
        {
            // Suppress provider disposal exceptions
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress disconnection exceptions
        }

        try
        {
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
        catch
        {
            // Suppress writer completion exceptions
        }

        _connectGate.Dispose();
        _writeGate.Dispose();

        try
        {
            switch (_provider)
            {
                case IAsyncDisposable asyncProvider:
                    await asyncProvider.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable provider:
                    provider.Dispose();
                    break;
            }
        }
        catch
        {
            // Suppress provider disposal exceptions
        }
    }
}
