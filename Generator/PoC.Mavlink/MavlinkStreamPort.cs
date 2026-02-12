using System.Buffers;
using System.Runtime.InteropServices;

namespace Mavlink;

public sealed class MavlinkStreamPort : IMavlinkPort
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public MavlinkStreamPort(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
#if NETSTANDARD2_1_OR_GREATER
        return _stream.ReadAsync(buffer, ct);
#else
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return new ValueTask<int>(
                _stream.ReadAsync(segment.Array!, segment.Offset, segment.Count, ct));
        }

        return ReadAsyncFallback(buffer, ct);
#endif
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
#if NETSTANDARD2_1_OR_GREATER
        return _stream.WriteAsync(data, ct);
#else
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
        {
            return new ValueTask(
                _stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, ct));
        }

        var array = data.ToArray();
        return new ValueTask(
            _stream.WriteAsync(array, 0, array.Length, ct));
#endif
    }

#if !NETSTANDARD2_1_OR_GREATER
    private async ValueTask<int> ReadAsyncFallback(Memory<byte> buffer, CancellationToken ct)
    {
        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int read = await _stream.ReadAsync(temp, 0, buffer.Length, ct).ConfigureAwait(false);
            if (read > 0)
            {
                new Span<byte>(temp, 0, read).CopyTo(buffer.Span);
            }
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }
#endif

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            if (_stream is IAsyncDisposable async)
            {
                await async.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _stream.Dispose();
            }
        }
    }
#endif
}
