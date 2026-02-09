using System.Buffers.Binary;

namespace Mavlink;

internal readonly ref struct MavlinkV1Frame
{
    private readonly ReadOnlySpan<byte> _raw;

    public MavlinkV1Frame(ReadOnlySpan<byte> raw) => _raw = raw;

    public byte PayloadLength => _raw[1];
    public byte Sequence => _raw[2];
    public byte SystemId => _raw[3];
    public byte ComponentId => _raw[4];
    public byte MessageId => _raw[5];

    public ReadOnlySpan<byte> Payload =>
        _raw.Slice(MavlinkConstants.HEADER_V1_LENGTH, PayloadLength);

    public ReadOnlySpan<byte> CrcRegion =>
        _raw.Slice(1, MavlinkConstants.HEADER_V1_LENGTH - 1 + PayloadLength);

    public ushort ReceivedCrc =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            _raw.Slice(MavlinkConstants.HEADER_V1_LENGTH + PayloadLength));
}
