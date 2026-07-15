using Mavlink.Dialects;

namespace Mavlink;

public sealed class MavlinkChannelOptions
{
    public IMavlinkPortProvider PortProvider { get; set; } = null!;

    public IMavlinkDialect Dialect { get; set; } = null!;

    public IReconnectPolicy ReconnectPolicy { get; set; } = NoReconnectPolicy.Instance;

    public bool EnableReceive { get; set; } = true;

    public TimeSpan? LinkTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan? SystemTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public MavlinkSigner? Signer { get; set; }

    public int DispatchChannelCapacity { get; set; } = 256;

    internal void Validate()
    {
        if (PortProvider is null)
        {
            throw new ArgumentNullException(nameof(PortProvider));
        }

        if (Dialect is null)
        {
            throw new ArgumentNullException(nameof(Dialect));
        }

        if (ReconnectPolicy is null)
        {
            throw new ArgumentNullException(nameof(ReconnectPolicy));
        }

        if (DispatchChannelCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DispatchChannelCapacity),
                DispatchChannelCapacity,
                "Channel capacity must be at least 1.");
        }

        if (!EnableReceive && !PortProvider.CanWrite)
        {
            throw new ArgumentException(
                "Channel would be inert: EnableReceive is false and the port provider " +
                "cannot write. Enable receive or use a writable transport.");
        }
    }
}
