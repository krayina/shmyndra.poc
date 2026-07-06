namespace Mavlink;

public sealed class MavlinkClientOptions
{
    public TimeSpan? WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public MavlinkPacketVersion DefaultSendVersion { get; set; } = MavlinkPacketVersion.V2;

    public MavlinkSigner? Signer { get; set; }

    public int DispatchChannelCapacity { get; set; } = 256;

    public MavlinkClientMode Mode { get; set; } = MavlinkClientMode.ReadWrite;

    internal void Validate()
    {
        if (DispatchChannelCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DispatchChannelCapacity),
                DispatchChannelCapacity,
                "Channel capacity must be at least 1.");
        }
    }
}
