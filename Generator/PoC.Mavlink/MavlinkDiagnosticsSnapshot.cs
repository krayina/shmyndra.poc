namespace Mavlink;

public readonly record struct MavlinkDiagnosticsSnapshot(
    long ReceivedPackets,
    long SentPackets,
    long ChannelOverflows,
    long CrcErrors,
    long UnknownMessages,
    long SequenceErrors)
{
    public static MavlinkDiagnosticsSnapshot operator - (MavlinkDiagnosticsSnapshot a, MavlinkDiagnosticsSnapshot b)
        => new
        (
            a.ReceivedPackets - b.ReceivedPackets,
            a.SentPackets - b.SentPackets,
            a.ChannelOverflows - b.ChannelOverflows,
            a.CrcErrors - b.CrcErrors,
            a.UnknownMessages - b.UnknownMessages,
            a.SequenceErrors - b.SequenceErrors
        );

    public override string ToString() =>
        $"Rx:{ReceivedPackets} Tx:{SentPackets} Overflow:{ChannelOverflows} " +
        $"CRC:{CrcErrors} Unknown:{UnknownMessages} SeqErr:{SequenceErrors}";
}