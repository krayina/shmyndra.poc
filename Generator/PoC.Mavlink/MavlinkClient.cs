using Mavlink.Dialects;
using System.Buffers;
using System.IO.Pipelines;

namespace Mavlink;

public sealed class MavlinkClient : IAsyncDisposable, IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly MavlinkChannel _channel;
    private int _disposed;

    public MavlinkDiagnostics Diagnostics => _channel.Diagnostics;
    public MavlinkConnectionState State => _channel.State;

    public MavlinkPacketVersion DefaultSendVersion
    {
        get => _channel.DefaultSendVersion;
        set => _channel.DefaultSendVersion = value;
    }

    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged
    {
        add => _channel.StateChanged += value;
        remove => _channel.StateChanged -= value;
    }

    public MavlinkClient(
        IMavlinkPortProvider portProvider,
        IReconnectPolicy reconnectPolicy,
        IMavlinkDialect dialect,
        byte systemId,
        byte componentId,
        MavlinkSigner? signer = null)
    {
        _connection = new MavlinkConnection(portProvider, reconnectPolicy);
        _channel = new MavlinkChannel(_connection, dialect, systemId, componentId, signer);
    }

    public static MavlinkClient Create(
        Func<CancellationToken, ValueTask<IMavlinkPort>> portFactory,
        IMavlinkDialect dialect,
        byte systemId,
        byte componentId,
        IReconnectPolicy? reconnectPolicy = null,
        MavlinkSigner? signer = null,
        bool canReconnect = true)
    {
        var provider = new DelegatePortProvider(portFactory) { CanReconnect = canReconnect };
        return new MavlinkClient(provider, reconnectPolicy ?? NoReconnectPolicy.Instance, dialect, systemId, componentId, signer);
    }

    public Task ConnectAsync(CancellationToken ct = default) => _connection.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _connection.DisconnectAsync(ct);

    public IDisposable Subscribe<T>(Action<T, MavlinkReceivedPacket> cb, Func<MavlinkReceivedPacket, bool>? filter = null) where T : struct, IMavlinkMessage
        => _channel.Subscribe(cb, filter);

    public IDisposable SubscribeAll(Action<IMavlinkMessage, MavlinkReceivedPacket> cb, Func<MavlinkReceivedPacket, bool>? filter = null)
        => _channel.SubscribeAll(cb, filter);

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default) where T : struct, IMavlinkMessage
        => _channel.SendAsync(message, ct);

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
        => _channel.SendAsync(message, ct);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(MavlinkClient));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _channel.Dispose();
    }
}
