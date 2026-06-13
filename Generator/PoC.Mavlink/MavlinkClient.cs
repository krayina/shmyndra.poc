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

    private readonly MavlinkDiagnostics _diagnostics = new();
    private readonly MavlinkFrameReader? _framer;
    private readonly CancellationTokenSource? _cts;
    private readonly MavlinkSender _sender;

    private readonly byte _systemId;
    private readonly byte _componentId;
    private readonly bool _readingEnabled;

    private readonly Task? _readTask;
    private int _disposed;

    private volatile MavlinkPacketVersion _defaultSendVersion;

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
        _sender = new MavlinkSender(_port, _systemId, _componentId, _signer);
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
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        var info = _dialect.GetInfo(typeof(T))
            ?? throw new ArgumentException($"Type {typeof(T).Name} not registered");
        return _sender.SendAsync(message, info, DefaultSendVersion, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var info = _dialect.GetInfo(message.GetType())
            ?? throw new ArgumentException($"Type {message.GetType().Name} not registered");
        return _sender.SendAsync(message, info, DefaultSendVersion, ct);
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
        _sender.Dispose();
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
        _sender.Dispose();

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
