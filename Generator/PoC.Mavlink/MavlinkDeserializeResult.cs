namespace Mavlink;

public enum MavlinkDeserializeResult : byte
{
    Success = 0,
    UnknownMessageId = 1,
    CrcMismatch = 2,
}