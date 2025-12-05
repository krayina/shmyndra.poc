using System.ComponentModel;

namespace Mavlink;

#if NETSTANDARD2_1_OR_GREATER
[EditorBrowsable(EditorBrowsableState.Never)]
#endif
public interface IMavlinkMessageInfo
{
	uint MessageId { get; }
	byte CrcExtra { get; }
	string Name { get; }
	Type MessageType { get; }

	int PayloadLength { get; }
	int PayloadLengthWithExtensions { get; }
	bool HasExtensions { get; }

	int SerializePayloadV1(IMavlinkMessage message, Span<byte> destination);
	int SerializePayloadV2(IMavlinkMessage message, Span<byte> destination);

	IMavlinkMessage DeserializePayloadV1(ReadOnlySpan<byte> payload);
	IMavlinkMessage DeserializePayloadV2(ReadOnlySpan<byte> payload);
}

#if NETSTANDARD2_1_OR_GREATER
[EditorBrowsable(EditorBrowsableState.Never)]
#endif
public interface IMavlinkMessageInfo<T> : IMavlinkMessageInfo where T : IMavlinkMessage
{
	IMavlinkPayloadSerializer<T> PayloadSerializer { get; }
}
