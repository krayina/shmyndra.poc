namespace Mavlink;

/// <summary>
/// Non-generic targeting metadata for boxed flows (queues, SubscribeAll
/// round-trips) — mirrors the generic/non-generic duality of
/// IMavlinkMessageInfo. Boxed input, boxed output: these paths are already
/// boxed by design, so nothing gets worse.
/// </summary>
public interface IMavlinkTargetedMessageInfo : IMavlinkMessageInfo
{
    IMavlinkMessage WithTarget(IMavlinkMessage message, byte targetSystem, byte targetComponent);
}

/// <summary>
/// Generic targeting metadata: the generated *MessageInfo companion of a
/// targeted message implements this alongside IMavlinkMessageInfo&lt;T&gt;.
///
/// This is where the stamping behaviour lives INSTEAD of the DTO — the same
/// architectural decision already proven by keeping serialization in the
/// companion rather than on the message. The generated body is a single
/// `with` expression: legal from external code because record-struct init
/// accessors are public, so the DTO needs no extra members.
///
/// WithTarget is a constrained generic call on a struct: no boxing, one
/// struct copy (the same by-value copy cost the message already pays when
/// passed into SendAsync).
/// </summary>
public interface IMavlinkTargetedMessageInfo<T> : IMavlinkTargetedMessageInfo
    where T : struct, IMavlinkTargetedMessage
{
    /// <summary>Returns a copy of the message with the target fields set.
    /// Always OVERWRITES existing values: in SendToAsync the target argument
    /// wins over whatever the user pre-set in the struct.</summary>
    T WithTarget(in T message, byte targetSystem, byte targetComponent);
}
