namespace Mavlink.Routing;

public enum MavlinkSystemState : byte
{
    Unknown = 0,
    Alive = 1,
    Silent = 2,
}

public readonly struct MavlinkSystemStateChange
{
    public MavlinkSystemStateChange(MavlinkSystemState oldState, MavlinkSystemState newState)
    {
        OldState = oldState;
        NewState = newState;
    }

    public MavlinkSystemState OldState { get; }
    public MavlinkSystemState NewState { get; }
}
