namespace Mavlink;

public enum MavlinkSessionVersion : byte
{
    Unknown = 0,
    V1 = 1,
    V2 = 2,
    Hybrid = 3
}

public enum MavlinkPacketVersion : byte
{
    V1 = 1,
    V2 = 2,
}