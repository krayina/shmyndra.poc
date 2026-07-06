using Mavlink.Dialects;
using System.Diagnostics;

namespace Mavlink;

public sealed class MavlinkChannel : IAsyncDisposable, IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkReceiver _receiver;
    private readonly MavlinkSender _sender;
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkDiagnostics _diagnostics;

    private volatile MavlinkPacketVersion _defaultSendVersion = MavlinkPacketVersion.V2;
    private int _disposed;

    public MavlinkChannel(
        IMavlinkConnection connection,
        IMavlinkDialect dialect,
        MavlinkReceiver receiver,
        MavlinkSender sender,
        MavlinkDispatcher dispatcher,
        MavlinkEventBus eventBus,
        MavlinkDiagnostics diagnostics)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

        _dispatcher.Start();
        _receiver.Start();
    }

    public IMavlinkDialect Dialect => _dialect;
    public MavlinkDiagnostics Diagnostics => _diagnostics;
    public MavlinkConnectionState State => _connection.State;

    public MavlinkPacketVersion DefaultSendVersion
    {
        get => _defaultSendVersion;
        set => _defaultSendVersion = value;
    }

    public Task ConnectAsync(CancellationToken ct = default) => _connection.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _connection.DisconnectAsync(ct);

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> cb,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        return _eventBus.Subscribe(cb, filter);
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> cb,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        ThrowIfDisposed();
        return _eventBus.SubscribeAll(cb, filter);
    }

    public ValueTask SendAsync<T>(
        T message,
        CancellationToken ct = default)
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

    public ValueTask SendAsync(
        IMavlinkMessage message,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var info = _dialect.GetInfo(message.GetType());
        if (info == null)
        {
            throw new ArgumentException($"{message.GetType().Name} not registered in dialect.");
        }

        return _sender.SendAsync(message, info, _defaultSendVersion, ct);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkChannel));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _receiver.Dispose();
        _dispatcher.Dispose();
        _sender.Dispose();
        _diagnostics.Dispose();

        await Task.CompletedTask;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
