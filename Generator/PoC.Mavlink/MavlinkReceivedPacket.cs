namespace Mavlink;

public readonly struct MavlinkReceivedPacket
{
    public uint MessageId { get; }
    public byte SenderSystemId { get; }
    public byte SenderComponentId { get; }
    public byte Sequence { get; }
    public MavlinkPacketVersion Version { get; }
    public bool IsSigned { get; }
    public byte PayloadLength { get; }

    private readonly MavlinkPayloadBuffer _payload;

#if NET8_0_OR_GREATER
    public ReadOnlySpan<byte> Payload => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in _payload._element0), PayloadLength);
#else
    public ReadOnlySpan<byte> Payload => _payload.AsSpan(PayloadLength);
#endif

    public MavlinkReceivedPacket(
        uint messageId,
        byte sysId,
        byte compId,
        byte seq,
        MavlinkPacketVersion version,
        bool isSigned,
        ReadOnlySpan<byte> payloadSpan)
    {
        MessageId = messageId;
        SenderSystemId = sysId;
        SenderComponentId = compId;
        Sequence = seq;
        Version = version;
        IsSigned = isSigned;
        PayloadLength = (byte)payloadSpan.Length;

#if NET8_0_OR_GREATER
        payloadSpan.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in _payload._element0), 255));
#else
        _payload.CopyFrom(payloadSpan);
#endif
    }
}
