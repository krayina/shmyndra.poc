namespace Mavlink.Transport;

public enum TcpEndpointMode
{
    /// <summary>
    /// Connect out to Host:Port (e.g. SITL's tcp:5760).
    /// </summary>
    Client,

    /// <summary>
    /// Bind Port and accept a single inbound connection at a time.
    /// </summary>
    Server,
}
