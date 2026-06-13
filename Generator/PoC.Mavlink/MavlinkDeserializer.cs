namespace Mavlink;

public static class MavlinkDeserializer
{
    public static T Deserialize<T>(
        in MavlinkReceivedPacket packet,
        IMavlinkMessageInfo<T> info)
        where T : struct, IMavlinkMessage
    {
        return packet.Version == MavlinkPacketVersion.V2
            ? info.PayloadSerializer.DeserializeV2(packet.Payload)
            : info.PayloadSerializer.DeserializeV1(packet.Payload);
    }

    public static IMavlinkMessage Deserialize(
        in MavlinkReceivedPacket packet,
        IMavlinkMessageInfo info)
    {
        return packet.Version == MavlinkPacketVersion.V2
            ? info.DeserializePayloadV2(packet.Payload)
            : info.DeserializePayloadV1(packet.Payload);
    }
}
