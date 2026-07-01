using Mavlink.Dialects;
using System.Diagnostics;

namespace Mavlink;

public sealed class MavlinkChannel : IAsyncDisposable, IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkReceiver _receiver;
    private readonly MavlinkSender _sender;
    private readonly MavlinkDiagnostics _diagnostics = new();
    private volatile MavlinkPacketVersion _defaultSendVersion = MavlinkPacketVersion.V2;
    private int _disposed;

    public IMavlinkDialect Dialect => _dialect;
    public MavlinkDiagnostics Diagnostics => _diagnostics;
    public MavlinkConnectionState State => _connection.State;

    public MavlinkPacketVersion DefaultSendVersion
    {
        get => _defaultSendVersion;
        set => _defaultSendVersion = value;
    }

    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged
    {
        add => _connection.StateChanged += value;
        remove => _connection.StateChanged -= value;
    }

    public MavlinkChannel(IMavlinkConnection connection, IMavlinkDialect dialect,
        byte systemId, byte componentId, MavlinkSigner? signer = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));

        _eventBus = new MavlinkEventBus(_dialect);
        _dispatcher = new MavlinkDispatcher(_eventBus);
        _receiver = new MavlinkReceiver(_connection, _dialect, _dispatcher, _diagnostics, _eventBus);
        _sender = new MavlinkSender(_connection, systemId, componentId, signer);

        _dispatcher.Start();
        _receiver.Start();
    }

    public Task ConnectAsync(CancellationToken ct = default) => _connection.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _connection.DisconnectAsync(ct);

    public IDisposable Subscribe<T>(Action<T, MavlinkReceivedPacket> cb,
        Func<MavlinkReceivedPacket, bool>? filter = null) where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        return _eventBus.Subscribe(cb, filter);
    }

    public IDisposable SubscribeAll(Action<IMavlinkMessage, MavlinkReceivedPacket> cb,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        ThrowIfDisposed();
        return _eventBus.SubscribeAll(cb, filter);
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default) where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        var info = _dialect.GetInfo(typeof(T))
            ?? throw new ArgumentException($"{typeof(T).Name} not registered in dialect.");
        return _sender.SendAsync(message, info, _defaultSendVersion, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var info = _dialect.GetInfo(message.GetType())
            ?? throw new ArgumentException($"{message.GetType().Name} not registered in dialect.");
        return _sender.SendAsync(message, info, _defaultSendVersion, ct);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MavlinkChannel));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        await _connection.DisposeAsync().ConfigureAwait(false);
        _receiver.Dispose();
        _dispatcher.Dispose();
        _sender.Dispose();
        _diagnostics.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
