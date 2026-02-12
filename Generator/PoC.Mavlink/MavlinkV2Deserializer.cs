using Mavlink.Dialects;

namespace Mavlink;

public static class MavlinkV2Deserializer
{
    public static MavlinkDeserializeResult TryDeserialize(
        ReadOnlySpan<byte> raw,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket context)
    {
        context = default;

        var frame = new MavlinkV2Frame(raw);

        var info = dialect.GetInfo(frame.MessageId);
        if (info == null)
        {
            return MavlinkDeserializeResult.UnknownMessageId;
        }

        ushort computed = X25Crc.Calculate(frame.CrcRegion);
        computed = X25Crc.Accumulate(computed, info.CrcExtra);
        if (frame.ReceivedCrc != computed)
        {
            return MavlinkDeserializeResult.CrcMismatch;
        }

        var message = info.DeserializePayloadV2(frame.Payload);

        context = new MavlinkReceivedPacket(
            message,
            frame.SystemId,
            frame.ComponentId,
            frame.Sequence,
            MavlinkPacketVersion.V2,
            frame.IsSigned);

        return MavlinkDeserializeResult.Success;
    }
}
