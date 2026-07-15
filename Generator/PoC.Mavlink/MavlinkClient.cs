using Mavlink.Dialects;
using System.Net.Sockets;

namespace Mavlink;

public sealed class MavlinkClient : IDisposable, IAsyncDisposable
{
    private readonly MavlinkChannel _channel;
    private readonly bool _ownsChannel;
    private int _sequence;
    private int _disposed;

    internal MavlinkClient(
        MavlinkChannel channel,
        byte systemId,
        byte componentId,
        MavlinkPacketVersion? defaultSendVersion,
        bool ownsChannel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        SystemId = systemId;
        ComponentId = componentId;
        DefaultSendVersion = defaultSendVersion;
        _ownsChannel = ownsChannel;
    }

    public MavlinkChannel Channel => _channel;
    public byte SystemId { get; }
    public byte ComponentId { get; }

    /// <summary>
    /// Wire version for outgoing frames. Null = auto:
    /// explicit SendAsync argument → this property → (Phase 3+) target's
    /// observed SessionVersion → V2. On a receive-disabled channel the auto
    /// path always resolves to V2; set this explicitly for V1-only receivers.
    /// </summary>
    public MavlinkPacketVersion? DefaultSendVersion { get; set; }
    public MavlinkConnectionState State => _channel.State;
    public MavlinkDiagnostics Diagnostics => _channel.Diagnostics;

    public event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged
    {
        add => _channel.StateChanged += value;
        remove => _channel.StateChanged -= value;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _channel.ConnectAsync(ct);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _channel.DisconnectAsync(ct);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();
        return _channel.Subscribe(callback, filter);
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default)
        where T : struct, IMavlinkMessage
        => SendAsync(message, version: null, ct);

    public ValueTask SendAsync<T>(
        T message,
        MavlinkPacketVersion? version,
        CancellationToken ct = default)
        where T : struct, IMavlinkMessage
    {
        ThrowIfDisposed();

        var info = _channel.Dialect.GetInfo(typeof(T))
            ?? throw new ArgumentException($"{typeof(T).Name} not registered in dialect.");

        return _channel.SendFrameAsync(
            message, info, NextSequence(), SystemId, ComponentId, ResolveVersion(version), ct);
    }

    public ValueTask SendAsync(
        IMavlinkMessage message,
        MavlinkPacketVersion? version = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var info = _channel.Dialect.GetInfo(message.GetType())
            ?? throw new ArgumentException($"{message.GetType().Name} not registered in dialect.");

        return _channel.SendFrameAsync(
            message, info, NextSequence(), SystemId, ComponentId, ResolveVersion(version), ct);
    }

    private MavlinkPacketVersion ResolveVersion(MavlinkPacketVersion? explicitVersion)
    {
        return explicitVersion
            ?? DefaultSendVersion
            ?? MavlinkPacketVersion.V2;
    }

    private byte NextSequence()
    {
        return (byte)Interlocked.Increment(ref _sequence);
    }

    public static MavlinkClient CreateUdp(
        string host,
        int port,
        IMavlinkDialect dialect,
        byte systemId = 255,
        byte componentId = 190,
        IReconnectPolicy? reconnectPolicy = null,
        Action<MavlinkChannelOptions>? configure = null)
    {
        var options = new MavlinkChannelOptions
        {
            Dialect = dialect,
            ReconnectPolicy = reconnectPolicy
                ?? new ExponentialBackoffPolicy(retryInitialConnect: true),
            PortProvider = new DelegatePortProvider(ct =>
            {
                // A fresh UdpClient per call — the provider contract.
                var udp = new UdpClient();
                udp.Connect(host, port);
                return new ValueTask<IMavlinkPort>(new MavlinkUdpPort(udp));
            }),
        };

        configure?.Invoke(options);

        var channel = MavlinkChannel.Create(options);
        return new MavlinkClient(channel, systemId, componentId,
            defaultSendVersion: null, ownsChannel: true);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkClient));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (_ownsChannel)
        {
            _channel.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (_ownsChannel)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
