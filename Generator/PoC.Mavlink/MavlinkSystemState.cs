namespace Mavlink.Routing;

public enum MavlinkSystemState : byte
{
    Unknown = 0,
    Alive = 1,
    Silent = 2,
}

public enum MavlinkSessionVersion : byte
{
    Unknown = 0,
    V1 = 1,
    V2 = 2,
    Hybrid = 3,
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
