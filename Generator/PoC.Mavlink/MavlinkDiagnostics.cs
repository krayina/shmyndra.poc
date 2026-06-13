using System.Collections.Concurrent;

namespace Mavlink;

public sealed class MavlinkDiagnostics : IDisposable
{
    private long _receivedPackets;
    private long _sentPackets;
    private long _channelOverflows;
    private long _crcErrors;
    private long _unknownMessages;
    private long _sequenceErrors;
    private long _badFrameErrors;

    private readonly ConcurrentDictionary<ushort, byte> _lastSequence = new();

    private Action<MavlinkDiagnosticsSnapshot>? _subscriber;
    private readonly object _subscribeLock = new();

    public MavlinkDiagnosticsSnapshot GetSnapshot()
        => new
        (
            ReceivedPackets: Interlocked.Read(ref _receivedPackets),
            SentPackets: Interlocked.Read(ref _sentPackets),
            ChannelOverflows: Interlocked.Read(ref _channelOverflows),
            CrcErrors: Interlocked.Read(ref _crcErrors),
            UnknownMessages: Interlocked.Read(ref _unknownMessages),
            SequenceErrors: Interlocked.Read(ref _sequenceErrors)
        );

    public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);
    public long SentPackets => Interlocked.Read(ref _sentPackets);

    /// <summary>
    /// Subscribe to batched diagnostics updates.
    /// Only one subscriber at a time (UI window pattern).
    /// Caller should marshal to UI thread and throttle if needed.
    /// </summary>
    public IDisposable Subscribe(Action<MavlinkDiagnosticsSnapshot> onChanged)
    {
        if (onChanged == null)
        {
            throw new ArgumentNullException(nameof(onChanged));
        }

        lock (_subscribeLock)
        {
            _subscriber = onChanged;
        }

        onChanged(GetSnapshot());

        return new MavlinkSubscription(() =>
        {
            lock (_subscribeLock)
            {
                _subscriber = null;
            }
        });
    }

    internal void OnReceived()
    {
        Interlocked.Increment(ref _receivedPackets);
        NotifyIfSubscribed();
    }

    internal void OnSent()
    {
        Interlocked.Increment(ref _sentPackets);
        NotifyIfSubscribed();
    }

    internal void OnChannelOverflow()
    {
        Interlocked.Increment(ref _channelOverflows);
        NotifyIfSubscribed();
    }

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
                Interlocked.Increment(ref _badFrameErrors);
                break;
        }
        NotifyIfSubscribed();
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
                Interlocked.Add(ref _sequenceErrors, gap);
            }
            return seq;
        });

        NotifyIfSubscribed();
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _receivedPackets, 0);
        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _channelOverflows, 0);
        Interlocked.Exchange(ref _crcErrors, 0);
        Interlocked.Exchange(ref _unknownMessages, 0);
        Interlocked.Exchange(ref _sequenceErrors, 0);
        _lastSequence.Clear();
        NotifyIfSubscribed();
    }

    private void NotifyIfSubscribed()
    {
        var handler = _subscriber;
        if (handler == null)
        {
            return;
        }    

        try
        {
            handler(GetSnapshot());
        }
        catch { /* subscriber fault isolation */ }
    }

    public void Dispose()
    {
        lock (_subscribeLock)
        {
            _subscriber = null;
        }
    }

    public override string ToString() => GetSnapshot().ToString();
}
