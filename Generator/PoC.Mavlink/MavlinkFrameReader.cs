namespace Mavlink;

#if !NETSTANDARD2_1_OR_GREATER
internal sealed class MavlinkFrameReader
{
    private byte[] _buffer;
    private int _head;
    private int _tail;

    public MavlinkFrameReader(int initialCapacity = 8192)
    {
        _buffer = new byte[initialCapacity];
    }

    internal byte[] RawBuffer => _buffer;

    public void Append(ReadOnlySpan<byte> source)
    {
        EnsureCapacity(source.Length);
        source.CopyTo(_buffer.AsSpan(_tail));
        _tail += source.Length;
    }

    /// <summary>
    /// Drops all buffered, not-yet-parsed bytes. Call between link sessions:
    /// the tail of a dead session must not be concatenated with the next one,
    /// otherwise resync can synthesize frames that never existed on the wire.
    /// Capacity is kept.
    /// </summary>
    public void Reset()
    {
        _head = 0;
        _tail = 0;
    }

    /// <summary>
    /// Locates the next complete candidate frame WITHOUT consuming it.
    /// On true: [frameOffset, frameLength) in RawBuffer is a full frame candidate.
    /// Caller MUST then call either Consume(frameLength) if the frame parsed OK,
    /// or SkipByte() if it failed — byte-wise resync, so a real frame hiding
    /// inside a false-magic span is never jumped over.
    /// </summary>
    public bool TryPeekFrame(
        out int frameOffset,
        out int frameLength,
        out MavlinkPacketVersion version)
    {
        frameOffset = 0;
        frameLength = 0;
        version = default;

        while (_head < _tail)
        {
            byte magic = _buffer[_head];
            int available = _tail - _head;

            if (magic == MavlinkConstants.MAGIC_V2)
            {
                if (available < MavlinkConstants.HEADER_V2_LENGTH)
                {
                    return false; // need more data
                }

                int payloadLen = _buffer[_head + MavlinkConstants.V2_PAYLOAD_LENGTH_OFFSET];
                bool signed = (_buffer[_head + MavlinkConstants.V2_INCOMPAT_FLAGS_OFFSET]
                               & (byte)MavlinkIncompatFlags.Signed) != 0;
                int total = MavlinkConstants.HEADER_V2_LENGTH
                            + payloadLen
                            + MavlinkConstants.CRC_LENGTH
                            + (signed ? MavlinkConstants.SIGNATURE_LENGTH : 0);

                if (available < total)
                {
                    return false;
                }

                frameOffset = _head;
                frameLength = total;
                version = MavlinkPacketVersion.V2;
                return true; // NOT consumed
            }

            if (magic == MavlinkConstants.MAGIC_V1)
            {
                if (available < MavlinkConstants.HEADER_V1_LENGTH)
                {
                    return false;
                }

                int payloadLen = _buffer[_head + MavlinkConstants.V1_PAYLOAD_LENGTH_OFFSET];
                int total = MavlinkConstants.HEADER_V1_LENGTH
                            + payloadLen
                            + MavlinkConstants.CRC_LENGTH;

                if (available < total)
                {
                    return false;
                }

                frameOffset = _head;
                frameLength = total;
                version = MavlinkPacketVersion.V1;
                return true; // NOT consumed
            }

            SkipToNextMagic();
        }

        return false;
    }

    /// <summary>
    /// Consume a successfully parsed frame.
    /// </summary>
    public void Consume(int frameLength)
    {
        _head += frameLength;
    }

    /// <summary>
    /// Failed parse at current magic: step one byte, resync from there.
    /// </summary>
    public void SkipByte()
    {
        _head++;
    }

    public void CompactIfNeeded()
    {
        if (_head == 0)
        {
            return;
        }

        if (_head >= _tail)
        {
            _head = 0;
            _tail = 0;
            return;
        }

        int len = _tail - _head;
        Buffer.BlockCopy(_buffer, _head, _buffer, 0, len);
        _tail = len;
        _head = 0;
    }

    private void SkipToNextMagic()
    {
        var remaining = _buffer.AsSpan(_head + 1, _tail - _head - 1);
        int idx = remaining.IndexOfAny(MavlinkConstants.MAGIC_V1, MavlinkConstants.MAGIC_V2);

        _head = idx >= 0 ? _head + 1 + idx : _tail;
    }

    private void EnsureCapacity(int additional)
    {
        if (_tail + additional <= _buffer.Length)
        {
            return;
        }

        CompactIfNeeded();

        if (_tail + additional <= _buffer.Length)
        {
            return;
        }

        int newSize = Math.Max(_buffer.Length * 2, _tail + additional);
        var newBuffer = new byte[newSize];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _tail);
        _buffer = newBuffer;
    }
}
#endif
