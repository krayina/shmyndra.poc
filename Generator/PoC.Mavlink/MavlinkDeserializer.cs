using Mavlink.Dialects;

namespace Mavlink;

internal static class MavlinkDeserializer
{
    public static MavlinkDeserializeResult TryDeserialize(
        ReadOnlySpan<byte> frame,
        MavlinkPacketVersion version,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket packet)
    {
        switch (version)
        {
            case MavlinkPacketVersion.V2:
                return MavlinkV2Deserializer.TryDeserialize(frame, dialect, out packet);
            case MavlinkPacketVersion.V1:
                return MavlinkV1Deserializer.TryDeserialize(frame, dialect, out packet);
            default:
                packet = default;
                return MavlinkDeserializeResult.CrcMismatch;
        }
    }
}
