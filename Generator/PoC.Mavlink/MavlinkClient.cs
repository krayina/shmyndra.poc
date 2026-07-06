using Mavlink.Dialects;

namespace Mavlink;

public sealed class MavlinkClient : IAsyncDisposable, IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkReceiver _receiver;
    private readonly MavlinkSender _sender;
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly ConnectionWatchdog? _watchdog;

    private volatile MavlinkPacketVersion _defaultSendVersion = MavlinkPacketVersion.V2;
    private int _disposed;

    private MavlinkClient(
        IMavlinkConnection connection,
        IMavlinkDialect dialect,
        MavlinkReceiver receiver,
        MavlinkSender sender,
        MavlinkDispatcher dispatcher,
        MavlinkEventBus eventBus,
        MavlinkDiagnostics diagnostics,
        ConnectionWatchdog? watchdog)
    {
        _connection = connection;
        _dialect = dialect;
        _receiver = receiver;
        _sender = sender;
        _dispatcher = dispatcher;
        _eventBus = eventBus;
        _diagnostics = diagnostics;
        _watchdog = watchdog;

        _connection.StateChanged += OnConnectionStateChanged;

        _dispatcher.Start();
        _receiver.Start();
    }

    public MavlinkDiagnostics Diagnostics => _diagnostics;
    public MavlinkConnectionState State => _connection.State;

    public MavlinkPacketVersion DefaultSendVersion
    {
        get => _defaultSendVersion;
        set => _defaultSendVersion = value;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return _connection.ConnectAsync(ct);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        return _connection.DisconnectAsync(ct);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> cb,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        return _eventBus.Subscribe(cb, filter);
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();

        var info = _dialect.GetInfo(typeof(T));
        if (info == null)
        {
            throw new ArgumentException($"{typeof(T).Name} not registered in dialect.");
        }

        return _sender.SendAsync(message, info, _defaultSendVersion, ct);
    }

    public static MavlinkClient Create(
        Func<CancellationToken, ValueTask<IMavlinkPort>> portFactory,
        IMavlinkDialect dialect,
        byte systemId,
        byte componentId,
        MavlinkClientOptions options,
        IReconnectPolicy? reconnectPolicy = null,
        MavlinkSigner? signer = null)
    {
        var provider = new DelegatePortProvider(portFactory);
        var policy = reconnectPolicy ?? NoReconnectPolicy.Instance;
        var connection = new MavlinkConnection(provider, policy);

        ConnectionWatchdog? watchdog = null;
        Action? onActivityCallback = null;

        if (options.WatchdogTimeout.HasValue)
        {
            watchdog = new ConnectionWatchdog(options.WatchdogTimeout.Value, () =>
            {
                connection.Abort();
                return Task.CompletedTask;
            });
            onActivityCallback = watchdog.NotifyActivity;
        }

        var eventBus = new MavlinkEventBus(dialect);
        var dispatcher = new MavlinkDispatcher(eventBus);
        var diagnostics = new MavlinkDiagnostics();
        var processingStage = new PacketProcessingStage(dispatcher, diagnostics, onActivityCallback);

        var receiver = new MavlinkReceiver(connection.Input, dialect, processingStage, processingStage);
        var sender = new MavlinkSender(connection, systemId, componentId, signer);

        return new MavlinkClient(
            connection,
            dialect,
            receiver,
            sender,
            dispatcher,
            eventBus,
            diagnostics,
            watchdog)
        {
            DefaultSendVersion = options.DefaultSendVersion
        };
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
            throw new ObjectDisposedException(nameof(MavlinkClient));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _connection.StateChanged -= OnConnectionStateChanged;

        _watchdog?.Dispose();
        _receiver.Dispose();
        _dispatcher.Dispose();
        _sender.Dispose();
        _diagnostics.Dispose();

        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
