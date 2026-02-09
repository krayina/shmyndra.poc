using Mavlink.Dialects;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mavlink;

#if NETSTANDARD2_1_OR_GREATER
public sealed class MavlinkClient : IDisposable, IAsyncDisposable
#else
public sealed class MavlinkClient : IDisposable
#endif
{
    private readonly Stream _stream;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkSigner? _signer;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private readonly MavlinkEventBus _eventBus = new MavlinkEventBus();
    private readonly MavlinkSessionState _sessionState = new MavlinkSessionState();
    private readonly MavlinkFrameReader _framer = new MavlinkFrameReader();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private readonly byte _systemId;
    private readonly byte _componentId;

    private Task? _readTask;
    private volatile bool _disposed;

    public MavlinkSessionState Session => _sessionState;

    public MavlinkPacketVersion DefaultSendVersion { get; set; } = MavlinkPacketVersion.V2;

    public Action<Exception>? OnHandlerError
    {
        get => _eventBus.OnError;
        set => _eventBus.OnError = value;
    }

    private MavlinkClient() { }

    public MavlinkClient(Stream stream, byte systemId, byte componentId, params IMavlinkDialect[] dialects)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _systemId = systemId;
        _componentId = componentId;
        _dialect = PrepareDialectOrThrow(dialects);
    }

    public MavlinkClient(Stream stream, byte systemId, byte componentId, MavlinkSigner signer, params IMavlinkDialect[] dialects)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _systemId = systemId;
        _componentId = componentId;
        _signer = signer;
        _dialect = PrepareDialectOrThrow(dialects);
    }

    public void StartReading()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MavlinkClient));
        }
        if (_readTask != null)
        {
            throw new InvalidOperationException("Reading already started.");
        }
        _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default)
        where T : IMavlinkMessage
    {
        return SendAsync(message, DefaultSendVersion, ct);
    }

    public ValueTask SendAsync<T>(T message, MavlinkPacketVersion version, CancellationToken ct = default)
        where T : IMavlinkMessage
    {
        var info = _dialect.GetInfo(typeof(T))
            ?? throw new ArgumentException($"Type {typeof(T).Name} not registered");

        return SendGuardedAsync(message, info, version, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
    {
        return SendAsync(message, DefaultSendVersion, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, MavlinkPacketVersion version, CancellationToken ct = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var info = _dialect.GetInfo(message.GetType())
            ?? throw new ArgumentException(
                $"Message type {message.GetType().Name} is not supported by the linked dialects.");

        return SendGuardedAsync(message, info, version, ct);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : IMavlinkMessage
    {
        return _eventBus.Subscribe(callback, filter);
    }

    public IDisposable Subscribe<T>(Action<T> callback)
        where T : IMavlinkMessage
    {
        return _eventBus.Subscribe<T>((msg, _) => callback(msg));
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        return _eventBus.SubscribeAll(callback, filter);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendGuardedAsync<T>(
        T message,
        IMavlinkMessageInfo info,
        MavlinkPacketVersion version,
        CancellationToken ct) where T : IMavlinkMessage
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MavlinkClient));

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);

        byte[]? buffer = null;

        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MavlinkClient));

            buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE);

            byte seq = _sessionState.NextSequence();
            int length = MavlinkSerializer.Serialize(
                message, info, seq, _systemId, _componentId,
                buffer, version, _signer);

#if NETSTANDARD2_1_OR_GREATER
            await _stream.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
#else
            await _stream.WriteAsync(buffer, 0, length, ct).ConfigureAwait(false);
#endif
        }
        finally
        {
            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer);

            try { _sendLock.Release(); }
            catch (ObjectDisposedException) { }
        }
    }


    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var readBuffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
#if NETSTANDARD2_1_OR_GREATER
                int read = await _stream.ReadAsync(readBuffer.AsMemory(), ct).ConfigureAwait(false);
#else
                int read = await _stream.ReadAsync(readBuffer, 0, readBuffer.Length, ct).ConfigureAwait(false);
#endif
                if (read == 0) break;

                ProcessReceivedBytes(readBuffer, read);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private void ProcessReceivedBytes(byte[] data, int count)
    {
        _framer.Append(data, 0, count);

        // SAFETY: RawBuffer reference is stable within this loop because
        // Compact/resize only happens inside Append, which is above.
        while (_framer.TryReadFrame(out int offset, out int length, out var version))
        {
            var frame = _framer.RawBuffer.AsSpan(offset, length);

            if (!MavlinkDeserializer.TryDeserialize(frame, version, _dialect, out var packet))
            {
                continue;
            }

            _sessionState.UpdateFromPacket(version, packet.IsSigned);
            _eventBus.Publish(packet);
        }
    }

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _sendLock.Dispose();
        _stream.Dispose();
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
            catch
            {
            }
        }

        _cts.Dispose();
        _sendLock.Dispose();

        if (_stream is IAsyncDisposable asyncStream)
        {
            await asyncStream.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _stream.Dispose();
        }
    }
#endif
}
