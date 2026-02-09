namespace Mavlink;

internal static class MavlinkSerializer
{
    public static int Serialize<T>(
        T message,
        IMavlinkMessageInfo info,
        byte seq,
        byte systemId,
        byte componentId,
        byte[] buffer,
        MavlinkPacketVersion version,
        MavlinkSigner? signer = null)
        where T : IMavlinkMessage
    {
        if (version == MavlinkPacketVersion.V1)
        {
            return info is IMavlinkMessageInfo<T> typed
                ? MavlinkV1Serializer.Serialize(message, typed, seq, systemId, componentId, buffer)
                : MavlinkV1Serializer.Serialize(message, info, seq, systemId, componentId, buffer);
        }
        else
        {
            return info is IMavlinkMessageInfo<T> typed
                ? MavlinkV2Serializer.Serialize(message, typed, seq, systemId, componentId, buffer, signer)
                : MavlinkV2Serializer.Serialize(message, info, seq, systemId, componentId, buffer, signer);
        }
    }
}
