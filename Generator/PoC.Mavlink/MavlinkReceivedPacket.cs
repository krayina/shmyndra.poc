namespace Mavlink;

public readonly struct MavlinkReceivedPacket
{
    public IMavlinkMessage Message { get; }
    public byte SenderSystemId { get; }
    public byte SenderComponentId { get; }
    public byte Sequence { get; }
    public MavlinkPacketVersion Version { get; }
    public bool IsSigned { get; }

    public MavlinkReceivedPacket(
        IMavlinkMessage message,
        byte sysId,
        byte compId,
        byte seq,
        MavlinkPacketVersion version,
        bool isSigned)
    {
        Message = message;
        SenderSystemId = sysId;
        SenderComponentId = compId;
        Sequence = seq;
        Version = version;
        IsSigned = isSigned;
    }
}
