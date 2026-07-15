using System.Runtime.CompilerServices;

namespace Mavlink.Routing;

public sealed class MavlinkComponentView
{
    private readonly MavlinkEventBus _eventBus;
    private long _lastSeenTicks;

    internal MavlinkComponentView(byte systemId, byte componentId, MavlinkEventBus eventBus)
    {
        SystemId = systemId;
        ComponentId = componentId;
        _eventBus = eventBus;
    }

    public byte SystemId { get; }

    public byte ComponentId { get; }

    public DateTime? LastSeenUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSeenTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        byte sys = SystemId;
        byte comp = ComponentId;
        Func<MavlinkReceivedPacket, bool> combined = filter == null
            ? p => p.SenderSystemId == sys && p.SenderComponentId == comp
            : p => p.SenderSystemId == sys && p.SenderComponentId == comp && filter(p);

        return _eventBus.Subscribe(callback, combined);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void NotifySeen(long nowTicks)
    {
        Interlocked.Exchange(ref _lastSeenTicks, nowTicks);
    }
}
