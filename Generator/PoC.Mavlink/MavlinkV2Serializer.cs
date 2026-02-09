using Mavlink.Dialects;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Mavlink;

public static class MavlinkV2Serializer
{
    #region Serialization
    // ----------------------------------------------------------------------------------------------
    // V2 Header: STX(1) + LEN(1) + INC(1) + CMP(1) + SEQ(1) + SYS(1) + COMP(1) + MSGID(3) = 10 bytes
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Serializes a message to MAVLink V2 packet (Typed version - Zero Boxing/Casting).
    /// </summary>
#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static int Serialize<T>(
        T message,
        IMavlinkMessageInfo<T> info,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer,
        MavlinkSigner? signer) where T : IMavlinkMessage
    {
        // 1. Serialize Payload directly
        var payloadSpan = buffer.Slice(MavlinkConstants.HEADER_V2_LENGTH);
        info.PayloadSerializer.SerializeV2(message, payloadSpan);

        // 2. Assemble Packet (Header + CRC)
        return AssemblePacket(
            info.PayloadLength,
            info.MessageId,
            info.CrcExtra,
            sequence,
            systemId,
            componentId,
            buffer,
            signer);
    }

    /// <summary>
    /// Serializes a message to MAVLink V2 packet (Untyped version - Interface call).
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
        Span<byte> buffer,
        MavlinkSigner? signer)
    {
        var payloadSpan = buffer.Slice(MavlinkConstants.HEADER_V2_LENGTH);
        info.SerializePayloadV2(message, payloadSpan);

        return AssemblePacket(
            info.PayloadLength,
            info.MessageId,
            info.CrcExtra,
            sequence,
            systemId,
            componentId,
            buffer,
            signer);
    }

    private static int AssemblePacket(
        int maxPayloadLen,
        uint msgId,
        byte crcExtra,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer,
        MavlinkSigner? signer)
    {
        bool isSigned = signer != null;
        var payloadBuffer = buffer.Slice(MavlinkConstants.HEADER_V2_LENGTH);

        // Payload Trimming
        int realPayloadLen = maxPayloadLen;
        while (realPayloadLen > 0 && payloadBuffer[realPayloadLen - 1] == 0)
        {
            realPayloadLen--;
        }

        // STX, LEN, INC, CMP, SEQ, SYS, COMP, MSGID(3)
        buffer[0] = MavlinkConstants.MAGIC_V2;      // 0xFD
        buffer[1] = (byte)realPayloadLen;
        buffer[2] = isSigned ? (byte)MavlinkIncompatFlags.Signed : (byte)0;
        buffer[3] = 0;                              // Compatibility Flags
        buffer[4] = sequence;
        buffer[5] = systemId;
        buffer[6] = componentId;

        // Message ID (3 bytes, Little Endian)
        buffer[7] = (byte)msgId;
        buffer[8] = (byte)(msgId >> 8);
        buffer[9] = (byte)(msgId >> 16);

        // CRC includes Header (excluding Magic) + Payload
        int lengthToCrc = MavlinkConstants.HEADER_V2_LENGTH + realPayloadLen;

        // Calculate CRC over [LEN..END_OF_PAYLOAD]
        ushort crc = X25Crc.Calculate(buffer.Slice(1, lengthToCrc - 1));
        crc = X25Crc.Accumulate(crc, crcExtra);

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(lengthToCrc), crc);

        // Total: Header + Payload + CRC (2)
        int totalLength = lengthToCrc + 2;

        // SIGNATURE (13 bytes)
        if (isSigned)
        {
            var packetWithCrc = buffer.Slice(0, totalLength);
            var signatureBlock = buffer.Slice(totalLength, 13);
            signer!.SignPacket(packetWithCrc, signatureBlock);
            totalLength += 13;
        }
        return totalLength;
    }
    #endregion

    #region Deserialization
    public static bool TryDeserialize(
        ReadOnlySpan<byte> raw,
        IMavlinkDialect dialect,
        out MavlinkReceivedPacket context)
    {
        context = default;

        var frame = new MavlinkV2Frame(raw);

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

        var message = info.DeserializePayloadV2(frame.Payload);

        context = new MavlinkReceivedPacket(
            message,
            frame.SystemId,
            frame.ComponentId,
            frame.Sequence,
            MavlinkPacketVersion.V2,
            frame.IsSigned);

        return true;
    }
    #endregion
}