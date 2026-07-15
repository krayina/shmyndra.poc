namespace Mavlink.Common;

public sealed class CommandLongMessageInfo : IMavlinkMessageInfo<CommandLongMavlinkMessage>, IMavlinkTargetedMessageInfo<CommandLongMavlinkMessage>
{
    public static readonly CommandLongMessageInfo Instance = new CommandLongMessageInfo();

    public uint MessageId => 76;
    public byte CrcExtra => 152;
    public string Name => "COMMAND_LONG";
    public Type MessageType => typeof(CommandLongMavlinkMessage);

    public int PayloadLength => 33;
    public int PayloadLengthWithExtensions => 33;
    public bool HasExtensions => false;

    public IMavlinkPayloadSerializer<CommandLongMavlinkMessage> PayloadSerializer { get; }
        = new Codecs.Payload.CommandLongPayloadSerializer();

    public int SerializePayloadV1(IMavlinkMessage message, Span<byte> destination)
    {
        if (message is CommandLongMavlinkMessage msg)
        {
            return PayloadSerializer.SerializeV1(msg, destination);
        }
        throw new ArgumentException($"Incorrect message type. Expected CommandLong, got {message.GetType().Name}");
    }

    public int SerializePayloadV2(IMavlinkMessage message, Span<byte> destination)
    {
        if (message is CommandLongMavlinkMessage msg)
        {
            return PayloadSerializer.SerializeV2(msg, destination);
        }
        throw new ArgumentException($"Incorrect message type. Expected CommandLong, got {message.GetType().Name}");
    }

    public IMavlinkMessage DeserializePayloadV1(ReadOnlySpan<byte> payload)
    {
        return PayloadSerializer.DeserializeV1(payload);
    }

    public IMavlinkMessage DeserializePayloadV2(ReadOnlySpan<byte> payload)
    {
        return PayloadSerializer.DeserializeV2(payload);
    }

    public CommandLongMavlinkMessage WithTarget(
        in CommandLongMavlinkMessage message, byte targetSystem, byte targetComponent)
    {
        return message with { TargetSystem = targetSystem, TargetComponent = targetComponent };
    }

    public IMavlinkMessage WithTarget(
        IMavlinkMessage message, byte targetSystem, byte targetComponent)
    {
        if (message is CommandLongMavlinkMessage msg)
        {
            return msg with { TargetSystem = targetSystem, TargetComponent = targetComponent };
        }
        throw new ArgumentException($"Incorrect message type. Expected CommandLong, got {message.GetType().Name}");
    }
}
