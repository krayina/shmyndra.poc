namespace Mavlink;

public interface IMavlinkMessageRouter
{
    bool TryRouteAndSend(MavlinkClient client, IMavlinkMessage message, CancellationToken ct, out ValueTask task);
}