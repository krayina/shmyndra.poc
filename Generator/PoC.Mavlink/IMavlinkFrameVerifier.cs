namespace Mavlink;

internal interface IMavlinkFrameVerifier
{
    bool Verify(ReadOnlySpan<byte> frame, in MavlinkReceivedPacket packet);
}
