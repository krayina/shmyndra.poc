namespace Mavlink;

public sealed class MavlinkConnectionStateChangedEventArgs : EventArgs
{
    public MavlinkConnectionState OldState { get; init; }
    public MavlinkConnectionState NewState { get; init; }
    public Exception? Error { get; init; }
}
