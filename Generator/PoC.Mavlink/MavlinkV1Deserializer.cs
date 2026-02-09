using Mavlink.Dialects;

namespace Mavlink;

public static class MavlinkV1Deserializer
{
    public static bool TryDeserialize(
        ReadOnlySpan<byte> raw,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket context)
    {
        context = default;

        var frame = new MavlinkV1Frame(raw);

        var info = dialect.GetInfo(frame.MessageId);
        if (info == null)
        {
            return false;
        }

        ushort computed = X25Crc.Calculate(frame.CrcRegion);
        computed = X25Crc.Accumulate(computed, info.CrcExtra);
        if (frame.ReceivedCrc != computed)
        {
            return false;
        }

        var message = info.DeserializePayloadV1(frame.Payload);

        context = new MavlinkReceivedPacket(
            message,
            frame.SystemId,
            frame.ComponentId,
            frame.Sequence,
            MavlinkPacketVersion.V1,
            isSigned: false);

        return true;
    }
}
