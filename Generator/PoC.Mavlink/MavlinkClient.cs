using Mavlink.Dialects;
using System.Buffers;

namespace Mavlink;

public sealed class MavlinkClient : IDisposable
#if NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly IMavlinkPort _port;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkSigner? _signer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly MavlinkEventBus _eventBus = new();
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkSessionState _sessionState = new();
    private readonly MavlinkFrameReader _framer = new();
    private readonly CancellationTokenSource _cts = new();

    private readonly byte _systemId;
    private readonly byte _componentId;

    private readonly object _startLock = new();
    private Task? _readTask;
    private volatile bool _started;
    private volatile bool _disposed;

    public MavlinkSessionState Session => _sessionState;
    public MavlinkPacketVersion DefaultSendVersion { get; set; } = MavlinkPacketVersion.V2;

    public Action<Exception>? ErrorReceived
    {
        get => _eventBus.ErrorReceived;
        set => _eventBus.ErrorReceived = value;
    }

    public Action? OnReadingStopped { get; set; }

    public MavlinkClient(
        IMavlinkPort port,
        byte systemId,
        byte componentId,
        params IMavlinkDialect[] dialects)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _systemId = systemId;
        _componentId = componentId;
        _dialect = PrepareDialectOrThrow(dialects);
        _dispatcher = new MavlinkDispatcher(_eventBus);
    }

    public MavlinkClient(
        IMavlinkPort port,
        byte systemId,
        byte componentId,
        MavlinkSigner signer,
        params IMavlinkDialect[] dialects)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _systemId = systemId;
        _componentId = componentId;
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _dialect = PrepareDialectOrThrow(dialects);
        _dispatcher = new MavlinkDispatcher(_eventBus);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : IMavlinkMessage
    {
        var sub = _eventBus.Subscribe(callback, filter);
        EnsureStarted();
        return sub;
    }

    public IDisposable Subscribe<T>(Action<T> callback)
        where T : IMavlinkMessage
    {
        var sub = _eventBus.Subscribe<T>((msg, _) => callback(msg));
        EnsureStarted();
        return sub;
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        var sub = _eventBus.SubscribeAll(callback, filter);
        EnsureStarted();
        return sub;
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default)
        where T : IMavlinkMessage
    {
        return SendAsync(message, DefaultSendVersion, ct);
    }

    public ValueTask SendAsync<T>(
        T message, MavlinkPacketVersion version, CancellationToken ct = default)
        where T : IMavlinkMessage
    {
        var info = _dialect.GetInfo(typeof(T))
            ?? throw new ArgumentException($"Type {typeof(T).Name} not registered");

        return SendCoreAsync(message, info, version, ct);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendCoreAsync<T>(
        T message, IMavlinkMessageInfo info,
        MavlinkPacketVersion version, CancellationToken ct)
        where T : IMavlinkMessage
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MavlinkClient));
        }

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        byte[]? buffer = null;
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MavlinkClient));
            }

            buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE);
            byte seq = _sessionState.NextSequence();
            int length = MavlinkSerializer.Serialize(
                message, info, seq, _systemId, _componentId,
                buffer, version, _signer);

            await _port.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }
        lock (_startLock)
        {
            if (_started)
            {
                return;
            }
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MavlinkClient));
            }

            _dispatcher.Start(_cts.Token);
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            _started = true;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await _port.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                EnqueueFrames(buffer, read);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _dispatcher.Complete();
            OnReadingStopped?.Invoke();
        }
    }

    private void EnqueueFrames(byte[] data, int count)
    {
        _framer.Append(data, 0, count);

        while (_framer.TryReadFrame(out int offset, out int length, out var version))
        {
            var frame = _framer.RawBuffer.AsSpan(offset, length);

            if (!MavlinkDeserializer.TryDeserialize(frame, version, _dialect, out var packet))
            {
                continue;
            }

            _sessionState.UpdateFromPacket(version, packet.IsSigned);
            _dispatcher.TryEnqueue(in packet);
        }

        _framer.CompactIfNeeded();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        _dispatcher.Dispose();
        _sendLock.Dispose();
        _port.Dispose();
    }

#if NETSTANDARD2_1_OR_GREATER
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();

        if (_readTask != null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch { }
        }

        await _dispatcher.DrainAsync().ConfigureAwait(false);

        _cts.Dispose();
        _dispatcher.Dispose();
        _sendLock.Dispose();

        if (_port is IAsyncDisposable asyncPort)
        {
            await asyncPort.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _port.Dispose();
        }
    }
#endif

    private static IMavlinkDialect PrepareDialectOrThrow(IMavlinkDialect[] dialects)
    {
        if (dialects == null || dialects.Length == 0)
        {
            throw new ArgumentException("At least one dialect is required.", nameof(dialects));
        }

        if (dialects.Length == 1)
        {
            return dialects[0] ?? throw new ArgumentException("Dialect cannot be null");
        }

        for (int i = 0; i < dialects.Length; i++)
        {
            if (dialects[i] == null)
            {
                throw new ArgumentException($"Dialect at index {i} is null.");
            }
        }

        return new MavlinkDialectCompositor(dialects);
    }
}
