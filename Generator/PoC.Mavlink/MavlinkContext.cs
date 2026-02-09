namespace Mavlink;

public readonly struct MavlinkContext
{
    public IMavlinkMessage Message { get; }

    public byte SenderSystemId { get; }
    public byte SenderComponentId { get; }
    public byte Sequence { get; }
    public MavlinkPacketVersion PacketVersion { get; }

    public MavlinkContext(IMavlinkMessage message, byte sysId, byte compId, byte seq, MavlinkPacketVersion version)
    {
        Message = message;
        SenderSystemId = sysId;
        SenderComponentId = compId;
        Sequence = seq;
        PacketVersion = version;
    }
}