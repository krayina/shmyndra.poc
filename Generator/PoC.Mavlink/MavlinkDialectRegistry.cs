using PoC.Mavlink;
using System.Collections.Concurrent;

namespace Mavlink;

public static class MavlinkDialectRegistry
{
    private static readonly Dictionary<uint, IMavlinkMessageInfo> _idToInfo = new();
    private static readonly Dictionary<Type, IMavlinkMessageInfo> _typeToInfo = new();
    private static IMavlinkMessageRouter[] _routers = Array.Empty<IMavlinkMessageRouter>();
    private static readonly object _lock = new();

    public static void RegisterMessage(IMavlinkMessageInfo info)
    {
        lock (_lock)
        {
            _idToInfo[info.MessageId] = info;
            _typeToInfo[info.MessageType] = info;
        }
    }

    public static void RegisterRouter(IMavlinkMessageRouter router)
    {
        lock (_lock)
        {
            var newRouters = new IMavlinkMessageRouter[_routers.Length + 1];
            Array.Copy(_routers, newRouters, _routers.Length);
            newRouters[_routers.Length] = router;
            _routers = newRouters;
        }
    }

    public static IMavlinkMessageInfo? GetInfo(uint msgId) => _idToInfo.GetValueOrDefault(msgId);
    public static IMavlinkMessageInfo? GetInfo(Type type) => _typeToInfo.GetValueOrDefault(type);

    public static bool TryRoute(this MavlinkClient client, IMavlinkMessage msg, CancellationToken ct, out ValueTask task)
    {
        var routers = _routers;

        for (int i = 0; i < routers.Length; i++)
        {
            if (routers[i].TryRouteAndSend(client, msg, ct, out task))
            {
                return true;
            }
        }

        task = default;
        return false;
    }
}
