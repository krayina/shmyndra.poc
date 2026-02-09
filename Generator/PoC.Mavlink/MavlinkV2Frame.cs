using System.Buffers.Binary;

namespace Mavlink;

internal readonly ref struct MavlinkV2Frame
{
    private readonly ReadOnlySpan<byte> _raw;

    public MavlinkV2Frame(ReadOnlySpan<byte> raw) => _raw = raw;

    public byte PayloadLength => _raw[1];
    public byte IncompatFlags => _raw[2];
    public byte CompatFlags => _raw[3];
    public byte Sequence => _raw[4];
    public byte SystemId => _raw[5];
    public byte ComponentId => _raw[6];

    public uint MessageId =>
        (uint)(_raw[7] | (_raw[8] << 8) | (_raw[9] << 16));

    public bool IsSigned =>
        (IncompatFlags & (byte)MavlinkIncompatFlags.Signed) != 0;

    public ReadOnlySpan<byte> Payload =>
        _raw.Slice(MavlinkConstants.HEADER_V2_LENGTH, PayloadLength);

    public ReadOnlySpan<byte> CrcRegion =>
        _raw.Slice(1, MavlinkConstants.HEADER_V2_LENGTH - 1 + PayloadLength);

    public ushort ReceivedCrc =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            _raw.Slice(MavlinkConstants.HEADER_V2_LENGTH + PayloadLength));
}
