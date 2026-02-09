using Mavlink.Dialects;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Mavlink;

public static class MavlinkV1Serializer
{
    #region Serialization
    // ---------------------------------------------------------------------------
    // V1 Header: STX(1) + LEN(1) + SEQ(1) + SYS(1) + COMP(1) + MSGID(1) = 6 bytes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Serializes a message to MAVLink V1 packet (Typed version).
    /// </summary>
#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static int SerializeV1<T>(
        T message,
        IMavlinkMessageInfo<T> info,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer) where T : IMavlinkMessage
    {
        var payloadSpan = buffer.Slice(MavlinkConstants.HEADER_V1_LENGTH);
        info.PayloadSerializer.SerializeV1(message, payloadSpan);

        return AssemblePacket(
            info.PayloadLength,
            info.MessageId,
            info.CrcExtra,
            sequence,
            systemId,
            componentId,
            buffer);
    }

    /// <summary>
    /// Serializes a message to MAVLink V1 packet (Untyped version).
    /// </summary>
#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static int Serialize(
        IMavlinkMessage message,
        IMavlinkMessageInfo info,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer)
    {
        var payloadSpan = buffer.Slice(MavlinkConstants.HEADER_V1_LENGTH);
        info.SerializePayloadV1(message, payloadSpan);

        return AssemblePacket(
            info.PayloadLength,
            info.MessageId,
            info.CrcExtra,
            sequence,
            systemId,
            componentId,
            buffer);
    }

    private static int AssemblePacket(
        int payloadLen,
        uint msgId,
        byte crcExtra,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer)
    {
        if (msgId > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(msgId), $"Message ID {msgId} is too large for Mavlink V1");
        }

        // STX, LEN, SEQ, SYS, COMP, MSGID(1)
        buffer[0] = MavlinkConstants.MAGIC_V1;      // 0xFE
        buffer[1] = (byte)payloadLen;               // V1 does NOT trim zeros
        buffer[2] = sequence;
        buffer[3] = systemId;
        buffer[4] = componentId;
        buffer[5] = (byte)msgId;

        // CRC includes Header (excluding Magic) + Payload
        int lengthToCrc = MavlinkConstants.HEADER_V1_LENGTH + payloadLen;

        // Calculate CRC over [LEN..END_OF_PAYLOAD]
        ushort crc = X25Crc.Calculate(buffer.Slice(1, lengthToCrc - 1));
        crc = X25Crc.Accumulate(crc, crcExtra);

        // Write CRC (Little Endian)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(lengthToCrc), crc);

        // Total: Header + Payload + CRC (2)
        return lengthToCrc + 2;
    }
    #endregion

    #region Deserialization
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
    #endregion
}