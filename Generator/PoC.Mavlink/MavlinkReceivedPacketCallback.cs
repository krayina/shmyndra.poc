using Mavlink.Dialects;

namespace Mavlink;

internal sealed class MavlinkReceivedPacketCallback : IMavlinkReceivedPacketCallback
{
    private readonly Action<IMavlinkMessage, MavlinkReceivedPacket> _callback;
    private readonly Func<MavlinkReceivedPacket, bool>? _filter;
    private readonly IMavlinkDialect _dialect;

    public MavlinkReceivedPacketCallback(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter,
        IMavlinkDialect dialect)
    {
        _callback = callback;
        _filter = filter;
        _dialect = dialect;
    }

    public void Invoke(in MavlinkReceivedPacket context)
    {
        if (_filter == null || _filter(context))
        {
            var info = _dialect.GetInfo(context.MessageId);
            if (info != null)
            {
                IMavlinkMessage message = MavlinkDeserializer.Deserialize(in context, info);
                _callback(message, context);
            }
        }
    }
}
