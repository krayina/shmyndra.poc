namespace Mavlink;

internal sealed class MavlinkReceivedPacketCallback<T> : IMavlinkReceivedPacketCallback
    where T : IMavlinkMessage
{
    private readonly Action<T, MavlinkReceivedPacket> _callback;
    private readonly Func<MavlinkReceivedPacket, bool>? _filter;

    public MavlinkReceivedPacketCallback(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter)
    {
        _callback = callback;
        _filter = filter;
    }

    public void Invoke(in MavlinkReceivedPacket context)
    {
        if (context.Message is T typed
            && (_filter == null || _filter(context)))
        {
            _callback(typed, context);
        }
    }
}
