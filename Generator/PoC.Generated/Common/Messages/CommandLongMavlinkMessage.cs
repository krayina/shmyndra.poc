namespace Mavlink.Common;

public readonly record struct CommandLongMavlinkMessage : IMavlinkTargetedMessage
{
    public float Param1 { get; init; }
    public float Param2 { get; init; }
    public float Param3 { get; init; }
    public float Param4 { get; init; }
    public float Param5 { get; init; }
    public float Param6 { get; init; }
    public float Param7 { get; init; }
    public MavCmd Command { get; init; }
    public byte TargetSystem { get; init; }
    public byte TargetComponent { get; init; }
    public byte Confirmation { get; init; }
}
