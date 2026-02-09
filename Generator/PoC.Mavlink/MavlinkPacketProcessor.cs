using Mavlink.Dialects;
using System.Buffers.Binary;

namespace Mavlink;

internal sealed class MavlinkPacketProcessor
{
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkSessionState _session;
    private readonly MavlinkDispatcher _dispatcher;

    public MavlinkPacketProcessor(
        IMavlinkDialect dialect,
        MavlinkSessionState session,
        MavlinkDispatcher dispatcher)
    {
        _dialect = dialect;
        _session = session;
        _dispatcher = dispatcher;
    }

    public void ProcessFrame(byte[] buffer, int offset, int length, MavlinkPacketVersion version)
    {
        var frame = buffer.AsSpan(offset, length);

        switch (version)
        {
            case MavlinkPacketVersion.V2:
                ProcessV2(frame);
                break;
            case MavlinkPacketVersion.V1:
                ProcessV1(frame);
                break;
        }
    }

    private void ProcessV2(ReadOnlySpan<byte> frame)
    {
        int payloadLen = frame[MavlinkConstants.V2_PAYLOAD_LENGTH_OFFSET];
        uint msgId = (uint)(frame[7] | (frame[8] << 8) | (frame[9] << 16));

        var info = _dialect.GetInfo(msgId);
        if (info == null) return;

        int crcOffset = MavlinkConstants.HEADER_V2_LENGTH + payloadLen;
        ushort received = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(crcOffset));
        ushort computed = X25Crc.Calculate(frame.Slice(1, crcOffset - 1));
        computed = X25Crc.Accumulate(computed, info.CrcExtra);
        if (received != computed) return;

        var payload = frame.Slice(MavlinkConstants.HEADER_V2_LENGTH, payloadLen);
        var message = info.DeserializePayloadV2(payload);

        bool signed = (frame[MavlinkConstants.V2_INCOMPAT_FLAGS_OFFSET]
                       & (byte)MavlinkIncompatFlags.Signed) != 0;

        _session.UpdateFromPacket(MavlinkPacketVersion.V2, signed);

        var context = new MavlinkContext(
            message,
            frame[MavlinkConstants.V2_SYSID_OFFSET],
            frame[MavlinkConstants.V2_COMPID_OFFSET],
            frame[MavlinkConstants.V2_SEQUENCE_OFFSET],
            MavlinkPacketVersion.V2);

        _dispatcher.Dispatch(context);
    }

    private void ProcessV1(ReadOnlySpan<byte> frame)
    {
        int payloadLen = frame[MavlinkConstants.V1_PAYLOAD_LENGTH_OFFSET];
        uint msgId = frame[MavlinkConstants.V1_MSGID_OFFSET];

        var info = _dialect.GetInfo(msgId);
        if (info == null) return;

        int crcOffset = MavlinkConstants.HEADER_V1_LENGTH + payloadLen;
        ushort received = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(crcOffset));
        ushort computed = X25Crc.Calculate(frame.Slice(1, crcOffset - 1));
        computed = X25Crc.Accumulate(computed, info.CrcExtra);
        if (received != computed) return;

        var payload = frame.Slice(MavlinkConstants.HEADER_V1_LENGTH, payloadLen);
        var message = info.DeserializePayloadV1(payload);

        _session.UpdateFromPacket(MavlinkPacketVersion.V1, false);

        var context = new MavlinkContext(
            message,
            frame[MavlinkConstants.V1_SYSID_OFFSET],
            frame[MavlinkConstants.V1_COMPID_OFFSET],
            frame[MavlinkConstants.V1_SEQUENCE_OFFSET],
            MavlinkPacketVersion.V1);

        _dispatcher.Dispatch(context);
    }
}