using Mavlink.Dialects;

namespace Mavlink;

internal static class MavlinkPacketParser
{
    public static MavlinkDeserializeResult TryParse(
        ReadOnlySpan<byte> frameBytes,
        MavlinkPacketVersion version,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket packet)
    {
        packet = default;
        return version switch
        {
            MavlinkPacketVersion.V2 => MavlinkV2PacketParser.TryParse(frameBytes, dialect, out packet),
            MavlinkPacketVersion.V1 => MavlinkV1PacketParser.TryParse(frameBytes, dialect, out packet),
            _ => MavlinkDeserializeResult.UnknownVersion
        };
    }
}
