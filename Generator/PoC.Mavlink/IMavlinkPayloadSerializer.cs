namespace Mavlink;

public interface IMavlinkPayloadSerializer<T> where T : IMavlinkMessage
{
	int SerializeV1(T message, Span<byte> destination);
	int SerializeV2(T message, Span<byte> destination);

	T DeserializeV1(ReadOnlySpan<byte> payload);
	T DeserializeV2(ReadOnlySpan<byte> payload);
}
