namespace Mavlink.Common;

internal sealed class CommonDialectRouter : IMavlinkMessageRouter
{
    public bool TryRouteAndSend(MavlinkClient client, IMavlinkMessage message, CancellationToken ct, out ValueTask task)
    {
        switch (message)
        {
            case HeartbeatMavlinkMessage msg:
                task = client.SendAsync(msg, ct);
                return true;

            default:
                task = default;
                return false;
        }
    }
}