namespace Mavlink.Common.Codecs.Payload;

public sealed class HeartbeatPayloadSerializer : IMavlinkPayloadSerializer<HeartbeatMavlinkMessage>
{
    public int SerializeV1(HeartbeatMavlinkMessage message, Span<byte> destination)
	{
		// serialization...
		return 9;
	}

    public int SerializeV2(HeartbeatMavlinkMessage message, Span<byte> destination)
	{
		return SerializeV1(message, destination);
	}

	public HeartbeatMavlinkMessage DeserializeV1(ReadOnlySpan<byte> payload)
	{
		// deserialization...
		return new HeartbeatMavlinkMessage();
	}

    public HeartbeatMavlinkMessage DeserializeV2(ReadOnlySpan<byte> payload)
    {
        // deserialization...
        return new HeartbeatMavlinkMessage();
    }

    // Optimization for JIT ?? -- I don't know if it's needed
    //   [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    //   private void BaseSerialization(HeartbeatMavlinkMessage message, Span<byte> destination)
    //{

    //}
}
