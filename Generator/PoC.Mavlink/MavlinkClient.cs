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
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkDispatcher? _dispatcher;

    private readonly MavlinkSessionState _sessionState = new();
    private readonly MavlinkDiagnostics _diagnostics = new();
    private readonly MavlinkFrameReader? _framer;
    private readonly CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly byte _systemId;
    private readonly byte _componentId;
    private readonly bool _readingEnabled;

    private readonly Task? _readTask;
    private int _disposed;

    private volatile MavlinkPacketVersion _defaultSendVersion;

    public MavlinkSessionState Session => _sessionState;
    public MavlinkDiagnostics Diagnostics => _diagnostics;

    public MavlinkPacketVersion DefaultSendVersion
    {
        get => _defaultSendVersion;
        set => _defaultSendVersion = value;
    }

    public event Action<Exception>? ErrorReceived
    {
        add => _eventBus.ErrorReceived += value;
        remove => _eventBus.ErrorReceived -= value;
    }

    public event Action? ReadingStopped;

    public MavlinkClient(
        IMavlinkPort port,
        byte systemId,
        byte componentId,
        MavlinkClientOptions? options = null,
        params IMavlinkDialect[] dialects)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _systemId = systemId;
        _componentId = componentId;

        _dialect = PrepareDialectOrThrow(dialects);
        _eventBus = new MavlinkEventBus(_dialect);

        options ??= new MavlinkClientOptions();
        options.Validate();

        _signer = options.Signer;
        _readingEnabled = options.Mode == MavlinkClientMode.ReadWrite;
        _defaultSendVersion = options.DefaultSendVersion;

        if (_readingEnabled)
        {
            _framer = new MavlinkFrameReader();
            _cts = new CancellationTokenSource();
            _dispatcher = new MavlinkDispatcher(_eventBus, options.DispatchChannelCapacity);
            _dispatcher.Start();
            _readTask = ReadLoopAsync(_cts.Token);
        }
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        ThrowIfReadingDisabled();
        return _eventBus.Subscribe(callback, filter);
    }

    public IDisposable Subscribe<T>(Action<T> callback)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        ThrowIfReadingDisabled();
        return _eventBus.Subscribe<T>((msg, _) => callback(msg));
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        byte senderSystemId,
        byte senderComponentId)
        where T : struct, IMavlinkMessage
    {
        return Subscribe<T>(callback,
            pkt => pkt.SenderSystemId == senderSystemId
                && pkt.SenderComponentId == senderComponentId);
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        ThrowIfDisposed();
        ThrowIfReadingDisabled();
        return _eventBus.SubscribeAll(callback, filter);
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
        ThrowIfDisposed();

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        byte[]? buffer = null;
        try
        {
            ThrowIfDisposed();

            buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE);
            byte seq = _sessionState.NextSequence();
            int length = MavlinkSerializer.Serialize(
                message, info, seq, _systemId, _componentId,
                buffer, version, _signer);

            await _port.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
            _diagnostics.OnSent();
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
            _dispatcher!.Complete();

            try
            {
                ReadingStopped?.Invoke();
            }
            catch (Exception ex)
            {
                _eventBus.RaiseError(ex);
            }
        }
    }

    private void EnqueueFrames(byte[] data, int count)
    {
        _framer!.Append(data, 0, count);

        while (_framer.TryReadFrame(out int offset, out int length, out var version))
        {
            var frame = _framer.RawBuffer.AsSpan(offset, length);

            var result = MavlinkPacketParser.TryParse(
                frame, version, _dialect, out var packet);

            if (result != MavlinkDeserializeResult.Success)
            {
                _diagnostics.OnDeserializeError(result);
                continue;
            }

            _diagnostics.OnReceived();
            _diagnostics.TrackSequence(
                packet.SenderSystemId,
                packet.SenderComponentId,
                packet.Sequence);
            _sessionState.UpdateFromPacket(version, packet.IsSigned);

            _dispatcher!.TryEnqueue(in packet);
        }

        _framer.CompactIfNeeded();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (_readingEnabled)
        {
            _cts!.Cancel();

            try
            {
                _readTask!.GetAwaiter().GetResult();
            }
            catch { }

            _dispatcher!.Complete();

            try
            {
                _dispatcher.DrainAsync().GetAwaiter().GetResult();
            }
            catch { }

            _cts.Dispose();
            _dispatcher.Dispose();
        }

        _diagnostics.Dispose();
        _sendLock.Dispose();
        _port.Dispose();
    }

#if NETSTANDARD2_1_OR_GREATER
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (_readingEnabled)
        {
            _cts!.Cancel();

            try
            {
                await _readTask!.ConfigureAwait(false);
            }
            catch { }

            _dispatcher!.Complete();
            await _dispatcher.DrainAsync().ConfigureAwait(false);

            _cts.Dispose();
            _dispatcher.Dispose();
        }

        _diagnostics.Dispose();
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

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkClient));
        }
    }

    private void ThrowIfReadingDisabled()
    {
        if (!_readingEnabled)
        {
            throw new InvalidOperationException(
                "Reading is disabled. Set Mode = ReadWrite in MavlinkClientOptions.");
        }
    }

    private static IMavlinkDialect PrepareDialectOrThrow(IMavlinkDialect[] dialects)
    {
        if (dialects == null || dialects.Length == 0)
        {
            throw new ArgumentException(
                "At least one dialect is required.", nameof(dialects));
        }

        if (dialects.Length == 1)
        {
            return dialects[0]
                ?? throw new ArgumentException("Dialect cannot be null");
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
