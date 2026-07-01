namespace Mavlink;

internal sealed class MavlinkConnection : IMavlinkConnection
{
    private readonly IMavlinkPortProvider _provider;
    private readonly IReconnectPolicy _policy;
    private readonly System.IO.Pipelines.Pipe _pipe;

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private IMavlinkPort? _port;
    private CancellationTokenSource? _life;
    private Task? _lifeTask;
    private Exception? _lastError;
    private MavlinkConnectionState _state = MavlinkConnectionState.Disconnected;
    private int _disposed;

    public MavlinkConnection(IMavlinkPortProvider provider, IReconnectPolicy policy)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _pipe = new System.IO.Pipelines.Pipe(System.IO.Pipelines.PipeOptions.Default);
    }

    public System.IO.Pipelines.PipeReader Input => _pipe.Reader;
    public MavlinkConnectionState State => _state;
    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged;

    private void SetState(MavlinkConnectionState value, Exception? error = null)
    {
        if (_state == value) return;
        var args = new MavlinkConnectionStateChangedEventArgs
        {
            OldState = _state,
            NewState = value,
            Error = error
        };
        _state = value;
        try { StateChanged?.Invoke(args); } catch { /* listener faults */ }
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_state is not (MavlinkConnectionState.Disconnected
            or MavlinkConnectionState.ConnectionLost)) return;

        _life = new CancellationTokenSource();
        SetState(MavlinkConnectionState.Connecting);
        try
        {
            await OpenPortAsync(ct).ConfigureAwait(false);
            SetState(MavlinkConnectionState.Connected);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            await StopAsync().ConfigureAwait(false);
            SetState(MavlinkConnectionState.Disconnected, ex);
            throw;
        }
        _lifeTask = LifecycleLoopAsync(_life.Token);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_life == null) return;
        SetState(MavlinkConnectionState.Disconnecting);
        _life.Cancel();
        if (_lifeTask != null) { try { await _lifeTask.ConfigureAwait(false); } catch { } }
        await StopAsync().ConfigureAwait(false);
        SetState(MavlinkConnectionState.Disconnected);
    }

    private async Task LifecycleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await PumpAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _lastError = ex; SetState(MavlinkConnectionState.ConnectionLost, ex); }

            await ClosePortAsync().ConfigureAwait(false);

            if (!_provider.CanReconnect || ct.IsCancellationRequested)
            { SetState(MavlinkConnectionState.ConnectionLost, _lastError); break; }

            SetState(MavlinkConnectionState.Reconnecting);
            int attempt = 1; bool ok = false;
            while (!ct.IsCancellationRequested)
            {
                var delay = _policy.GetDelay(attempt++, _lastError);
                if (!delay.HasValue) break;

                try
                {
                    if (delay.Value > TimeSpan.Zero)
                        await Task.Delay(delay.Value, ct).ConfigureAwait(false);
                    await OpenPortAsync(ct).ConfigureAwait(false);
                    ok = true;
                    SetState(MavlinkConnectionState.Connected);
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { _lastError = ex; }
            }
            if (!ok) { SetState(MavlinkConnectionState.ConnectionLost, _lastError); break; }
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var port = _port!;
        var writer = _pipe.Writer;

#if NETSTANDARD2_1_OR_GREATER
    // Шлях без копіювання (Zero-copy) для нових платформ, якщо порт підтримує PipeReader
    if (port.Reader is { } src)
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
            if (flush.IsCompleted || res.IsCompleted) break;
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
                if (n == 0) break;

                var mem = writer.GetMemory(n);
                bufArray.AsSpan(0, n).CopyTo(mem.Span);
                writer.Advance(n);

                var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (flush.IsCompleted) break;
            }
        }
        finally
        {
            pool.Return(bufArray);
        }
    }

    private async Task OpenPortAsync(CancellationToken ct)
    {
        _port = await _provider.CreatePortAsync(ct).ConfigureAwait(false);
        _lastError = null;
    }

    private async Task ClosePortAsync()
    {
        var port = _port; _port = null;
        if (port == null) return;
        try
        {
            if (port is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
            else port.Dispose();
        }
        catch { /* best effort */ }
    }

    private async Task StopAsync()
    {
        await ClosePortAsync().ConfigureAwait(false);
        _life?.Dispose(); _life = null;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var port = _port;
        if (_state != MavlinkConnectionState.Connected || port == null)
            throw new InvalidOperationException($"Cannot write: state = {_state}.");
        return WriteLockedAsync(port, data, ct);
    }

    private async ValueTask WriteLockedAsync(
        IMavlinkPort port, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try { await port.WriteAsync(data, ct).ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MavlinkConnection));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        await DisconnectAsync().ConfigureAwait(false);
        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        _pipe.Reader.Complete();
        _writeGate.Dispose();
    }
}