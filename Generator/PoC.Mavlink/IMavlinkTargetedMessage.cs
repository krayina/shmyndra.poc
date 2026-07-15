namespace Mavlink;

/// <summary>
/// Marks a message whose MAVLink XML definition contains target_system /
/// target_component payload fields (COMMAND_LONG, MISSION_*, PARAM_* …).
///
/// This interface adds ZERO members to the generated DTO: both properties
/// already exist on the struct because they are ordinary payload fields from
/// the dialect XML. The declaration line (`: IMavlinkTargetedMessage`) is the
/// only thing the code generator emits extra — the same style as the empty
/// IMavlinkMessage marker.
///
/// Its sole purpose is the compile-time constraint on
/// MavlinkClient.SendToAsync: "you can only address something that is
/// physically addressable". All BEHAVIOUR (stamping the target into a copy)
/// lives in the generated metadata companion — see
/// <see cref="IMavlinkTargetedMessageInfo{T}"/> — keeping the DTO pure.
///
/// Note on broadcast: nothing here handles it, because nothing needs to.
/// target_system = 0 IS broadcast per the MAVLink spec, and 0 is the default
/// value of the fields — a message nobody addressed is already a broadcast.
/// Plain SendAsync sends messages exactly as constructed.
/// </summary>
public interface IMavlinkTargetedMessage : IMavlinkMessage
{
    byte TargetSystem { get; }
    byte TargetComponent { get; }
}
