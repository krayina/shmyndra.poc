namespace Mavlink.Transport;

public enum UdpEndpointMode
{
    /// <summary>
    /// UDPCL. Connected" UDP: send datagrams to Host:Port, receive replies from that
    /// peer only.
    /// </summary>
    Client,

    /// <summary>
    /// UDP. Bind the local Port and accept datagrams from anyone; outgoing frames go
    /// to the most recent sender. MissionPlanner calls this mode simply.
    /// </summary>
    Listen,
}
