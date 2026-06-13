namespace Mavlink;

internal sealed class MavlinkReceivedPacketCallback<T> : IMavlinkReceivedPacketCallback
    where T : struct, IMavlinkMessage
{
    private readonly Action<T, MavlinkReceivedPacket> _callback;
    private readonly Func<MavlinkReceivedPacket, bool>? _filter;
    private readonly IMavlinkMessageInfo<T> _messageInfo;

    public MavlinkReceivedPacketCallback(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter,
        IMavlinkMessageInfo<T> messageInfo)
    {
        _callback = callback;
        _filter = filter;
        _messageInfo = messageInfo;
    }

    public void Invoke(in MavlinkReceivedPacket context)
    {
        if (context.MessageId == _messageInfo.MessageId
            && (_filter == null || _filter(context)))
        {
            T typedMessage = MavlinkDeserializer.Deserialize(in context, _messageInfo);
            _callback(typedMessage, context);
        }
    }
}
