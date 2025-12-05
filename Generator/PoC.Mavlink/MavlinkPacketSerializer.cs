using System.Buffers.Binary;

namespace Mavlink;

public static class MavlinkPacketSerializer
{
    // V2 Header: STX(1) + LEN(1) + INC(1) + CMP(1) + SEQ(1) + SYS(1) + COMP(1) + MSGID(3) = 10 bytes
    // V1 Header: STX(1) + LEN(1) + SEQ(1) + SYS(1) + COMP(1) + MSGID(1) = 6 bytes

    public static int SerializeV2<T>(
        T message,
        IMavlinkMessageInfo<T> info,
        byte sequence,
        byte systemId,
        byte componentId,
        Span<byte> buffer,
        bool sign = false) where T : IMavlinkMessage
    {
        var payloadBuffer = buffer.Slice(MavlinkConstants.HEADER_V2_LENGTH);
        info.PayloadSerializer.Serialize(message, payloadBuffer);
        int payloadLen = info.PayloadLength;
        while (payloadLen > 0 && payloadBuffer[payloadLen - 1] == 0)
        {
            payloadLen--;
        }

        buffer[0] = MavlinkConstants.MAGIC_V2;
        buffer[1] = (byte)payloadLen;
        buffer[2] = 0;
        buffer[3] = 0;
        buffer[4] = sequence;
        buffer[5] = systemId;
        buffer[6] = componentId;
        uint msgId = info.MessageId;
        buffer[7] = (byte)msgId;
        buffer[8] = (byte)(msgId >> 8);
        buffer[9] = (byte)(msgId >> 16);

        int lengthToCrc = MavlinkConstants.HEADER_V2_LENGTH - 1 + payloadLen;
        ushort crc = CrcX25.Calculate(buffer.Slice(1, lengthToCrc));
        crc = CrcX25.Accumulate(info.CrcExtra, crc);
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.Slice(MavlinkConstants.HEADER_V2_LENGTH + payloadLen),
            crc
        );

        return MavlinkConstants.HEADER_V2_LENGTH + payloadLen + 2;
    }
}