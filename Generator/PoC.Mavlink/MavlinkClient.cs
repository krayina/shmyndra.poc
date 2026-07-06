using Mavlink.Dialects;

namespace Mavlink;

public sealed class MavlinkClient : IAsyncDisposable, IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly MavlinkChannel _channel;
    private readonly ConnectionWatchdog? _watchdog;
    private int _disposed;

    private MavlinkClient(IMavlinkConnection connection, MavlinkChannel channel, ConnectionWatchdog? watchdog)
    {
        _connection = connection;
        _channel = channel;
        _watchdog = watchdog;

        _connection.StateChanged += OnConnectionStateChanged;
    }

    public MavlinkDiagnostics Diagnostics => _channel.Diagnostics;
    public MavlinkConnectionState State => _channel.State;

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

        var channel = new MavlinkChannel(connection, receiver, sender, dispatcher, eventBus, diagnostics)
        {
            DefaultSendVersion = options.DefaultSendVersion
        };

        return new MavlinkClient(connection, channel, watchdog);
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
        return _channel.Subscribe(cb, filter);
    }

    public ValueTask SendAsync<T>(
        T message,
        CancellationToken ct = default)
        where T : struct, IMavlinkMessage
    {
        return _channel.SendAsync(message, ct);
    } 

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _connection.StateChanged -= OnConnectionStateChanged;

        _watchdog?.Dispose();
        await _channel.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
