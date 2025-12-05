namespace Mavlink.Common.Codecs.Metadata;

public sealed class HeartbeatMessageInfo : IMavlinkMessageInfo<HeartbeatMavlinkMessage>
{
	public static readonly HeartbeatMessageInfo Instance = new HeartbeatMessageInfo();

	public uint MessageId => 0;
	public byte CrcExtra => 50;
	public string Name => "HEARTBEAT";
	public Type MessageType => typeof(HeartbeatMavlinkMessage);

	public int PayloadLength => 9;
	public int PayloadLengthWithExtensions => 9;
	public bool HasExtensions => false;

	public IMavlinkPayloadSerializer<HeartbeatMavlinkMessage> PayloadSerializer { get; }
        = new Payload.HeartbeatPayloadSerializer();

    public int SerializePayloadV1(IMavlinkMessage message, Span<byte> destination)
    {
        if (message is HeartbeatMavlinkMessage msg)
        {
            return PayloadSerializer.SerializeV1(msg, destination);
        }
        throw new ArgumentException($"Incorrect message type. Expected Heartbeat, got {message.GetType().Name}");
    }

    public int SerializePayloadV2(IMavlinkMessage message, Span<byte> destination)
    {
        if (message is HeartbeatMavlinkMessage msg)
        {
            return PayloadSerializer.SerializeV2(msg, destination);
        }
        throw new ArgumentException($"Incorrect message type...");
    }

    public IMavlinkMessage DeserializePayloadV1(ReadOnlySpan<byte> payload)
    {
        return PayloadSerializer.DeserializeV1(payload);
    }

    public IMavlinkMessage DeserializePayloadV2(ReadOnlySpan<byte> payload)
    {
        return PayloadSerializer.DeserializeV2(payload);
    }
}
