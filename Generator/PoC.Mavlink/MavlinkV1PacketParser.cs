using Mavlink.Dialects;

namespace Mavlink;

internal static class MavlinkV1PacketParser
{
    public static MavlinkDeserializeResult TryParse(
        ReadOnlySpan<byte> raw,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket packet)
    {
        packet = default;

        if (raw.Length < MavlinkConstants.HEADER_V1_LENGTH + 2)
        {
            return MavlinkDeserializeResult.InvalidFrameLength;
        }

        var frame = new MavlinkV1Frame(raw);

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

        packet = new MavlinkReceivedPacket(
            frame.MessageId,
            frame.SystemId,
            frame.ComponentId,
            frame.Sequence,
            MavlinkPacketVersion.V1,
            isSigned: false,
            frame.Payload);

        return MavlinkDeserializeResult.Success;
    }
}
