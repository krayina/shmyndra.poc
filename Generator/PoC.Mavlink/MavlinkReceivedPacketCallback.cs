namespace Mavlink;

internal sealed class MavlinkReceivedPacketCallback : IMavlinkReceivedPacketCallback
{
    private readonly Action<IMavlinkMessage, MavlinkReceivedPacket> _callback;
    private readonly Func<MavlinkReceivedPacket, bool>? _filter;

    public MavlinkReceivedPacketCallback(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter)
    {
        _callback = callback;
        _filter = filter;
    }

    public void Invoke(in MavlinkReceivedPacket context)
    {
        if (_filter == null || _filter(context))
        {
            _callback(context.Message, context);
        }
    }
}