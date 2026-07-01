using System.Buffers;
using System.Runtime.InteropServices;

namespace Mavlink;

public sealed class MavlinkStreamPort : IMavlinkPort
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

#if NETSTANDARD2_1_OR_GREATER
    public System.IO.Pipelines.PipeReader? Reader { get; }
#endif

    public MavlinkStreamPort(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
#if NETSTANDARD2_1_OR_GREATER
        Reader = System.IO.Pipelines.PipeReader.Create(
            _stream,
            new System.IO.Pipelines.StreamPipeReaderOptions(leaveOpen: leaveOpen)
        );
#endif
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
#if NETSTANDARD2_1_OR_GREATER
        return _stream.ReadAsync(buffer, ct);
#else
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return new ValueTask<int>(
                _stream.ReadAsync(
                    segment.Array!,
                    segment.Offset,
                    segment.Count,
                    ct
                )
            );
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
                _stream.WriteAsync(
                    segment.Array!,
                    segment.Offset, 
                    segment.Count,
                    ct
                )
            );
        }
        var array = data.ToArray();
        return new ValueTask(
            _stream.WriteAsync(array, 0, array.Length, ct)
        );
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

    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            if (_stream is IAsyncDisposable asyncStream)
            {
                await asyncStream.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _stream.Dispose();
            }
        }
    }
}
