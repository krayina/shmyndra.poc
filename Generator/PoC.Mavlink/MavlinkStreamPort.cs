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
        return new ValueTask<int>(
            _stream.ReadAsync(
                buffer.ToArray(), 0, buffer.Length, ct)); // спрощено
#endif
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
#if NETSTANDARD2_1_OR_GREATER
        return _stream.WriteAsync(data, ct);
#else
        var array = data.ToArray();
        return new ValueTask(
            _stream.WriteAsync(array, 0, array.Length, ct));
#endif
    }

    public void Dispose()
    {
        if (!_leaveOpen) _stream.Dispose();
    }

#if NETSTANDARD2_1_OR_GREATER
    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            if (_stream is IAsyncDisposable async)
                await async.DisposeAsync().ConfigureAwait(false);
            else
                _stream.Dispose();
        }
    }
#endif
}
