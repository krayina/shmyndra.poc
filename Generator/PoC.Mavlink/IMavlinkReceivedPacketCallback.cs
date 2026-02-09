namespace Mavlink;

internal interface IMavlinkReceivedPacketCallback
{
    void Invoke(in MavlinkReceivedPacket context);
}