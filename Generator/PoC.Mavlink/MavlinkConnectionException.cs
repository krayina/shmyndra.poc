namespace Mavlink;

public sealed class MavlinkConnectionException : Exception
{
    public MavlinkConnectionException(string message, Exception? inner = null)
        : base(message, inner) { }
}