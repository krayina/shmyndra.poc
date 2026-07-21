namespace Mavlink;

public interface IMavlinkPacketListener
{
    void OnPacketReceived(in MavlinkReceivedPacket packet);
}

public interface IMavlinkParserErrorListener
{
    void OnParserError(MavlinkDeserializeResult result);

    void OnReceiverFault(Exception exception);
}
