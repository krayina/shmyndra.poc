namespace Mavlink;

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

    public void Append(byte[] source, int offset, int count)
    {
        EnsureCapacity(count);
        Buffer.BlockCopy(source, offset, _buffer, _tail, count);
        _tail += count;
    }

    public bool TryReadFrame(
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
                    return false;

                int payloadLen = _buffer[_head + MavlinkConstants.V2_PAYLOAD_LENGTH_OFFSET];

                bool signed = (_buffer[_head + MavlinkConstants.V2_INCOMPAT_FLAGS_OFFSET]
                               & (byte)MavlinkIncompatFlags.Signed) != 0;

                int total = MavlinkConstants.HEADER_V2_LENGTH
                            + payloadLen
                            + MavlinkConstants.CRC_LENGTH
                            + (signed ? MavlinkConstants.SIGNATURE_LENGTH : 0);

                if (available < total)
                    return false;

                frameOffset = _head;
                frameLength = total;
                version = MavlinkPacketVersion.V2;
                _head += total;
                return true;
            }

            if (magic == MavlinkConstants.MAGIC_V1)
            {
                if (available < MavlinkConstants.HEADER_V1_LENGTH)
                    return false;

                int payloadLen = _buffer[_head + MavlinkConstants.V1_PAYLOAD_LENGTH_OFFSET];
                int total = MavlinkConstants.HEADER_V1_LENGTH
                            + payloadLen
                            + MavlinkConstants.CRC_LENGTH;

                if (available < total)
                    return false;

                frameOffset = _head;
                frameLength = total;
                version = MavlinkPacketVersion.V1;
                _head += total;
                return true;
            }

            _head++;
        }

        return false;
    }

    private void EnsureCapacity(int additional)
    {
        if (_tail + additional <= _buffer.Length)
            return;

        Compact();

        if (_tail + additional <= _buffer.Length)
            return;

        int newSize = Math.Max(_buffer.Length * 2, _tail + additional);
        var newBuffer = new byte[newSize];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _tail);
        _buffer = newBuffer;
    }

    private void Compact()
    {
        if (_head == 0) return;

        int len = _tail - _head;
        if (len > 0)
        {
            Buffer.BlockCopy(_buffer, _head, _buffer, 0, len);
        }

        _tail = len;
        _head = 0;
    }
}
