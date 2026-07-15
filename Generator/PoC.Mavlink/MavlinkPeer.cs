using Mavlink.Routing;

namespace Mavlink;

public sealed class MavlinkPeer
{
    private readonly MavlinkClient _client;
    private readonly MavlinkSystemView _system;
    private readonly MavlinkComponentView? _component;

    internal MavlinkPeer(MavlinkClient client, MavlinkSystemView system, MavlinkComponentView? component)
    {
        _client = client;
        _system = system;
        _component = component;
    }

    public MavlinkSystemView System => _system;

    public MavlinkComponentView? Component => _component;

    public MavlinkSystemState State => _system.State;

    public MavlinkSessionVersion SessionVersion => _system.SessionVersion;

    public ValueTask SendAsync<T>(
        T message,
        MavlinkPacketVersion? version = null,
        CancellationToken ct = default)
        where T : struct, IMavlinkTargetedMessage
    {
        return _component != null
            ? _client.SendToAsync(message, _component, version, ct)
            : _client.SendToAsync(message, _system, version, ct);
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        return _component != null
            ? _component.Subscribe(callback, filter)
            : _system.Subscribe(callback, filter);
    }
}
