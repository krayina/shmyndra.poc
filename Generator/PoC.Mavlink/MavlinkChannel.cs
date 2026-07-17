using Mavlink.Dialects;
using Mavlink.Routing;

namespace Mavlink;

public sealed partial class MavlinkChannel : IDisposable, IAsyncDisposable
{
    private readonly MavlinkConnection _connection;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkReceiver? _receiver;
    private readonly MavlinkDispatcher? _dispatcher;
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly ConnectionWatchdog? _watchdog;
    private readonly MavlinkNodeRegistry? _registry;
    private readonly MavlinkSigner? _signer;
    private readonly bool _canWrite;
    private readonly bool _receiveEnabled;
    private readonly byte[] _sendBuffer = new byte[MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE];
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private int _disposed;

    private MavlinkChannel(
        MavlinkConnection connection,
        IMavlinkDialect dialect,
        MavlinkReceiver? receiver,
        MavlinkDispatcher? dispatcher,
        MavlinkEventBus eventBus,
        MavlinkDiagnostics diagnostics,
        ConnectionWatchdog? watchdog,
        MavlinkNodeRegistry? registry,
        MavlinkSigner? signer,
        bool canWrite,
        bool receiveEnabled)
    {
        _connection = connection;
        _dialect = dialect;
        _receiver = receiver;
        _dispatcher = dispatcher;
        _eventBus = eventBus;
        _diagnostics = diagnostics;
        _watchdog = watchdog;
        _registry = registry;
        _signer = signer;
        _canWrite = canWrite;
        _receiveEnabled = receiveEnabled;

        _connection.StateChanged += OnConnectionStateChanged;

        _dispatcher?.Start();
        _receiver?.Start();
    }

    public static MavlinkChannel Create(MavlinkChannelOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();

        var provider = options.PortProvider;
        var connection = new MavlinkConnection(
            provider,
            options.ReconnectPolicy,
            pumpToPipe: options.EnableReceive);

        MavlinkReceiver? receiver = null;
        MavlinkDispatcher? dispatcher = null;
        ConnectionWatchdog? watchdog = null;
        MavlinkNodeRegistry? registry = null;

        var eventBus = new MavlinkEventBus(options.Dialect);
        var diagnostics = new MavlinkDiagnostics();

        if (options.EnableReceive)
        {
            Action? onActivity = null;

            if (options.LinkTimeout.HasValue)
            {
                watchdog = new ConnectionWatchdog(options.LinkTimeout.Value, () =>
                {
                    connection.Abort();
                    return Task.CompletedTask;
                });
                onActivity = watchdog.NotifyActivity;
            }

            if (options.SystemTimeout.HasValue)
            {
                registry = new MavlinkNodeRegistry(eventBus, options.SystemTimeout.Value);
            }

            dispatcher = new MavlinkDispatcher(eventBus, options.DispatchChannelCapacity);
            var stage = new PacketProcessingStage(dispatcher, diagnostics, registry, onActivity);
            receiver = new MavlinkReceiver(connection.Input, options.Dialect, stage, stage);
        }

        return new MavlinkChannel(
            connection,
            options.Dialect,
            receiver,
            dispatcher,
            eventBus,
            diagnostics,
            watchdog,
            registry,
            options.Signer,
            canWrite: provider.CanWrite,
            receiveEnabled: options.EnableReceive);
    }

    public IMavlinkDialect Dialect => _dialect;

    public MavlinkDiagnostics Diagnostics => _diagnostics;

    public MavlinkConnectionState State => _connection.State;

    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged
    {
        add => _connection.StateChanged += value;
        remove => _connection.StateChanged -= value;
    }

    public event Action<MavlinkSystemView>? SystemDiscovered
    {
        add
        {
            var reg = _registry ?? ThrowNoRegistry();
            reg.SystemDiscovered += value;
        }
        remove
        {
            if (_registry != null)
            {
                _registry.SystemDiscovered -= value;
            }
        }
    }

    public MavlinkSystemView GetSystem(byte systemId)
    {
        ThrowIfDisposed();
        var reg = _registry ?? ThrowNoRegistry();
        return reg.GetSystem(systemId);
    }

    public IReadOnlyCollection<MavlinkSystemView> Systems
        => _registry?.Systems ?? Array.Empty<MavlinkSystemView>();

    private MavlinkNodeRegistry ThrowNoRegistry()
    {
        throw new InvalidOperationException(_receiveEnabled
            ? "Per-system tracking is disabled: MavlinkChannelOptions.SystemTimeout is null."
            : "Per-system tracking requires receive: MavlinkChannelOptions.EnableReceive is false.");
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _connection.ConnectAsync(ct);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _connection.DisconnectAsync(ct);
    }

    public MavlinkClient CreateClient(
        byte systemId,
        byte componentId,
        MavlinkPacketVersion? defaultSendVersion = null)
    {
        ThrowIfDisposed();
        return new MavlinkClient(this, systemId, componentId, defaultSendVersion, ownsChannel: false);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        ThrowIfReceiveDisabled();
        return _eventBus.Subscribe(callback, filter);
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        ThrowIfDisposed();
        ThrowIfReceiveDisabled();
        return _eventBus.SubscribeAll(callback, filter);
    }

#if NET6_0_OR_GREATER
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
#endif
    internal async ValueTask SendFrameAsync<T>(
        T message,
        IMavlinkMessageInfo info,
        byte sequence,
        byte systemId,
        byte componentId,
        MavlinkPacketVersion version,
        CancellationToken ct)
        where T : IMavlinkMessage
    {
        ThrowIfDisposed();
        ThrowIfNotWritable();

        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            int len = MavlinkSerializer.Serialize(
                message, info, sequence, systemId, componentId, _sendBuffer, version, _signer);

            await _connection.WriteAsync(_sendBuffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _sendGate.Release();
            }
        }
    }

#if NET6_0_OR_GREATER
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
#endif
    internal async ValueTask SendFrameAsync(
        IMavlinkMessage message,
        IMavlinkMessageInfo info,
        byte sequence,
        byte systemId,
        byte componentId,
        MavlinkPacketVersion version,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ThrowIfNotWritable();

        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            int len = MavlinkSerializer.Serialize(
                message, info, sequence, systemId, componentId, _sendBuffer, version, _signer);

            await _connection.WriteAsync(_sendBuffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _sendGate.Release();
            }
        }
    }

    private void OnConnectionStateChanged(MavlinkConnectionStateChangedEventArgs args)
    {
        if (_watchdog == null)
        {
            return;
        }

        if (args.NewState == MavlinkConnectionState.Connected)
        {
            _watchdog.Start();
        }
        else
        {
            _watchdog.Stop();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkChannel));
        }
    }

    private void ThrowIfNotWritable()
    {
        if (!_canWrite)
        {
            throw new InvalidOperationException(
                "This channel's transport is read-only (IMavlinkPortProvider.CanWrite is false).");
        }
    }

    private void ThrowIfReceiveDisabled()
    {
        if (!_receiveEnabled)
        {
            throw new InvalidOperationException(
                "Receive is disabled on this channel (MavlinkChannelOptions.EnableReceive is false).");
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _connection.StateChanged -= OnConnectionStateChanged;
        _watchdog?.Dispose();
        _connection.Dispose();
        _receiver?.Dispose();
        _registry?.Dispose();
        _dispatcher?.Dispose();
        _sendGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _connection.StateChanged -= OnConnectionStateChanged;

        if (_watchdog != null)
        {
            await _watchdog.DisposeAsync().ConfigureAwait(false);
        }

        await _connection.DisposeAsync().ConfigureAwait(false);

        if (_receiver != null)
        {
            await _receiver.DisposeAsync().ConfigureAwait(false);
        }

        if (_registry != null)
        {
            await _registry.DisposeAsync().ConfigureAwait(false);
        }

        if (_dispatcher != null)
        {
            _dispatcher.Complete();
            await _dispatcher.DisposeAsync().ConfigureAwait(false);
        }

        _sendGate.Dispose();
    }
}
