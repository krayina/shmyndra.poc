using System.Collections.Concurrent;

namespace Mavlink;

public sealed class MavlinkDiagnostics
{
    private long _receivedPackets;
    private long _sentPackets;
    private long _bytesReceived;
    private long _bytesSent;
    private long _channelOverflows;
    private long _crcErrors;
    private long _unknownMessages;
    private long _sequenceErrors;
    private long _badFrameErrors;
    private long _signatureFailures;
    private long _lastPacketUtcTicks;
    private long _receiverFaults;
    private long _frameListenerFaults;
    private volatile Exception? _lastReceiverFault;
    private volatile Exception? _lastFrameListenerFault;

    private readonly ConcurrentDictionary<ushort, byte> _lastSequence = new();

    /// <summary>
    /// Gaps larger than this are treated as reordering/duplication rather than
    /// loss. MAVLink sequence numbers wrap at 256, so a "gap" of 250 is far more
    /// likely to be a late/duplicate datagram (UDP) than 250 genuinely lost frames.
    /// </summary>
    private const int MaxPlausibleSequenceGap = 128;

    public MavlinkDiagnosticsSnapshot GetSnapshot()
    {
        var lastTicks = Interlocked.Read(ref _lastPacketUtcTicks);

        return new MavlinkDiagnosticsSnapshot(
            ReceivedPackets: Interlocked.Read(ref _receivedPackets),
            SentPackets: Interlocked.Read(ref _sentPackets),
            BytesReceived: Interlocked.Read(ref _bytesReceived),
            BytesSent: Interlocked.Read(ref _bytesSent),
            ChannelOverflows: Interlocked.Read(ref _channelOverflows),
            CrcErrors: Interlocked.Read(ref _crcErrors),
            UnknownMessages: Interlocked.Read(ref _unknownMessages),
            SequenceErrors: Interlocked.Read(ref _sequenceErrors),
            BadFrameErrors: Interlocked.Read(ref _badFrameErrors),
            SignatureFailures: Interlocked.Read(ref _signatureFailures),
            ReceiverFaults: Interlocked.Read(ref _receiverFaults),
            FrameListenerFaults: Interlocked.Read(ref _frameListenerFaults),
            LastPacketUtc: lastTicks == 0
                ? null
                : new DateTime(lastTicks, DateTimeKind.Utc));
    }

    public long ReceiverFaults => Interlocked.Read(ref _receiverFaults);
    public Exception? LastReceiverFault => _lastReceiverFault;
    public long FrameListenerFaults => Interlocked.Read(ref _frameListenerFaults);
    public Exception? LastFrameListenerFault => _lastFrameListenerFault;
    public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);
    public long SentPackets => Interlocked.Read(ref _sentPackets);

    internal void OnReceived(int wireBytes)
    {
        Interlocked.Increment(ref _receivedPackets);
        Interlocked.Add(ref _bytesReceived, wireBytes);
        Interlocked.Exchange(ref _lastPacketUtcTicks, DateTime.UtcNow.Ticks);
    }

    internal void OnSent(int wireBytes)
    {
        Interlocked.Increment(ref _sentPackets);
        Interlocked.Add(ref _bytesSent, wireBytes);
    }

    internal void OnChannelOverflow()
        => Interlocked.Increment(ref _channelOverflows);

    internal void OnSignatureFailure()
        => Interlocked.Increment(ref _signatureFailures);

    internal void OnDeserializeError(MavlinkDeserializeResult result)
    {
        switch (result)
        {
            case MavlinkDeserializeResult.CrcMismatch:
                Interlocked.Increment(ref _crcErrors);
                break;
            case MavlinkDeserializeResult.UnknownMessageId:
                Interlocked.Increment(ref _unknownMessages);
                break;
            case MavlinkDeserializeResult.UnknownVersion:
            case MavlinkDeserializeResult.InvalidFrameLength:
            case MavlinkDeserializeResult.UnknownMagicByte:
            case MavlinkDeserializeResult.UnsupportedIncompatFlags:
                Interlocked.Increment(ref _badFrameErrors);
                break;
        }
    }

    internal void OnReceiverFault(Exception ex)
    {
        _lastReceiverFault = ex;
        Interlocked.Increment(ref _receiverFaults);
    }

    /// <summary>
    /// Reports a fault thrown by a user-supplied <see cref="IMavlinkRawFrameListener"/>.
    /// The fault is swallowed on the hot path so it cannot kill the read loop or fail
    /// a send; this counter is the only trace it leaves, so surface it in your UI.
    /// </summary>
    internal void OnFrameListenerFault(Exception ex)
    {
        _lastFrameListenerFault = ex;
        Interlocked.Increment(ref _frameListenerFaults);
    }

    internal void TrackSequence(byte sysId, byte compId, byte seq)
    {
        ushort key = (ushort)((sysId << 8) | compId);

        _lastSequence.AddOrUpdate(key, seq, (_, lastSeq) =>
        {
            byte expected = unchecked((byte)(lastSeq + 1));
            if (seq != expected)
            {
                int gap = (seq - expected + 256) % 256;

                // Large gaps are ambiguous: on UDP they usually mean a reordered or
                // duplicated datagram, not mass loss. Count only plausible gaps.
                if (gap < MaxPlausibleSequenceGap)
                {
                    Interlocked.Add(ref _sequenceErrors, gap);
                }
            }
            return seq;
        });
    }

    /// <summary>
    /// Drops per-component sequence state. Call on (re)connect: the first frame of a
    /// new session is unrelated to the last frame of the previous one, and comparing
    /// them would charge up to 255 phantom losses to <c>SequenceErrors</c>.
    /// </summary>
    internal void ResetSequenceTracking() => _lastSequence.Clear();

    public void Reset()
    {
        Interlocked.Exchange(ref _receivedPackets, 0);
        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _bytesReceived, 0);
        Interlocked.Exchange(ref _bytesSent, 0);
        Interlocked.Exchange(ref _channelOverflows, 0);
        Interlocked.Exchange(ref _crcErrors, 0);
        Interlocked.Exchange(ref _unknownMessages, 0);
        Interlocked.Exchange(ref _sequenceErrors, 0);
        Interlocked.Exchange(ref _badFrameErrors, 0);
        Interlocked.Exchange(ref _signatureFailures, 0);
        Interlocked.Exchange(ref _lastPacketUtcTicks, 0);
        Interlocked.Exchange(ref _receiverFaults, 0);
        Interlocked.Exchange(ref _frameListenerFaults, 0);
        _lastSequence.Clear();
        _lastReceiverFault = null;
        _lastFrameListenerFault = null;
    }

    public override string ToString() => GetSnapshot().ToString();
}
