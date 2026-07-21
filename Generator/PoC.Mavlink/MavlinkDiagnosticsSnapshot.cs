namespace Mavlink;

public readonly record struct MavlinkDiagnosticsSnapshot(
    long ReceivedPackets,
    long SentPackets,
    long BytesReceived,
    long BytesSent,
    long ChannelOverflows,
    long CrcErrors,
    long UnknownMessages,
    long SequenceErrors,
    long BadFrameErrors,
    long SignatureFailures,
    long ReceiverFaults,
    long FrameListenerFaults,
    DateTime? LastPacketUtc)
{
    public static MavlinkDiagnosticsSnapshot operator -(
        MavlinkDiagnosticsSnapshot a, MavlinkDiagnosticsSnapshot b)
        => new(
            a.ReceivedPackets - b.ReceivedPackets,
            a.SentPackets - b.SentPackets,
            a.BytesReceived - b.BytesReceived,
            a.BytesSent - b.BytesSent,
            a.ChannelOverflows - b.ChannelOverflows,
            a.CrcErrors - b.CrcErrors,
            a.UnknownMessages - b.UnknownMessages,
            a.SequenceErrors - b.SequenceErrors,
            a.BadFrameErrors - b.BadFrameErrors,
            a.SignatureFailures - b.SignatureFailures,
            a.ReceiverFaults - b.ReceiverFaults,
            a.FrameListenerFaults - b.FrameListenerFaults,
            a.LastPacketUtc);

    public MavlinkDiagnosticsRates PerSecond(TimeSpan elapsed)
    {
        double s = elapsed.TotalSeconds;
        if (s <= 0)
        {
            return default;
        }

        long totalExpected = ReceivedPackets + SequenceErrors;
        double loss = totalExpected > 0
            ? 100.0 * SequenceErrors / totalExpected
            : 0.0;

        return new MavlinkDiagnosticsRates(
            RxPacketsPerSec: ReceivedPackets / s,
            TxPacketsPerSec: SentPackets / s,
            RxBytesPerSec: BytesReceived / s,
            TxBytesPerSec: BytesSent / s,
            LossPercent: loss);
    }

    public override string ToString() =>
        $"Rx:{ReceivedPackets} Tx:{SentPackets} RxB:{BytesReceived} TxB:{BytesSent} " +
        $"Overflow:{ChannelOverflows} CRC:{CrcErrors} Unknown:{UnknownMessages} " +
        $"SeqErr:{SequenceErrors} BadFrame:{BadFrameErrors} SigFail:{SignatureFailures} " +
        $"RxFault:{ReceiverFaults} ListenerFault:{FrameListenerFaults}";
}
