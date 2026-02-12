namespace Mavlink;

public enum MavlinkClientMode : byte
{
    /// <summary>
    /// Full duplex: read loop and dispatcher are active.
    /// </summary>
    ReadWrite = 0,

    /// <summary>
    /// Send only: no read loop or dispatcher started.
    /// Subscriptions will throw <see cref="InvalidOperationException"/>.
    /// Diagnostics will track only sent packets.
    /// </summary>
    WriteOnly = 1,
}
