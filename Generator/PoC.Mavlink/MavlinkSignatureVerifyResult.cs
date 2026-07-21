namespace Mavlink;

public enum MavlinkSignatureVerifyResult
{
    Valid = 0,
    BadSignature,
    TimestampReplay,
    NewStreamTimestampOutOfRange,
}
