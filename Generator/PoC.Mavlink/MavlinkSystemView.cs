using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Mavlink.Routing;

public sealed class MavlinkSystemView
{
    private readonly MavlinkEventBus _eventBus;
    private readonly ConcurrentDictionary<byte, MavlinkComponentView> _components = new();
    private readonly Func<byte, MavlinkComponentView> _componentFactory;

    private long _lastSeenTicks;          // 0 = never seen
    private int _seenV1;                  // 0/1, set-once
    private int _seenV2;                  // 0/1, set-once

    private MavlinkSystemState _state = MavlinkSystemState.Unknown;
    private bool _announced;

    internal MavlinkSystemView(byte systemId, MavlinkEventBus eventBus)
    {
        SystemId = systemId;
        _eventBus = eventBus;
        _componentFactory = id => new MavlinkComponentView(systemId, id, eventBus);
    }

    public byte SystemId { get; }

    public MavlinkSystemState State => _state;

    public DateTime? LastSeenUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSeenTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public MavlinkSessionVersion SessionVersion
    {
        get
        {
            bool v1 = Volatile.Read(ref _seenV1) != 0;
            bool v2 = Volatile.Read(ref _seenV2) != 0;
            return (v1, v2) switch
            {
                (true, true) => MavlinkSessionVersion.Hybrid,
                (true, false) => MavlinkSessionVersion.V1,
                (false, true) => MavlinkSessionVersion.V2,
                _ => MavlinkSessionVersion.Unknown,
            };
        }
    }

    public event Action<MavlinkSystemView, MavlinkSystemStateChange>? StateChanged;

    public IReadOnlyCollection<MavlinkComponentView> Components
        => (IReadOnlyCollection<MavlinkComponentView>)_components.Values;

    public MavlinkComponentView GetComponent(byte componentId)
        => _components.GetOrAdd(componentId, _componentFactory);

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : struct, IMavlinkMessage
    {
        byte id = SystemId;
        Func<MavlinkReceivedPacket, bool> combined = filter == null
            ? p => p.SenderSystemId == id
            : p => p.SenderSystemId == id && filter(p);

        return _eventBus.Subscribe(callback, combined);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void OnPacket(in MavlinkReceivedPacket packet, long nowTicks)
    {
        Interlocked.Exchange(ref _lastSeenTicks, nowTicks);

        if (packet.Version == MavlinkPacketVersion.V2)
        {
            if (Volatile.Read(ref _seenV2) == 0) Volatile.Write(ref _seenV2, 1);
        }
        else
        {
            if (Volatile.Read(ref _seenV1) == 0) Volatile.Write(ref _seenV1, 1);
        }

        GetComponent(packet.SenderComponentId).NotifySeen(nowTicks);
    }

    internal bool Scan(long nowTicks, long timeoutTicks)
    {
        var lastSeen = Interlocked.Read(ref _lastSeenTicks);
        if (lastSeen == 0)
        {
            return false;
        }

        bool discovered = !_announced;
        _announced = true;

        var next = nowTicks - lastSeen > timeoutTicks
            ? MavlinkSystemState.Silent
            : MavlinkSystemState.Alive;

        if (next != _state)
        {
            var change = new MavlinkSystemStateChange(_state, next);
            _state = next;

            try
            {
                StateChanged?.Invoke(this, change);
            }
            catch
            {
                // Listener faults must not kill the scan loop.
            }
        }

        return discovered;
    }
}
