namespace Mavlink;

public interface IMavlinkPort : IDisposable
#if NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

#if NETSTANDARD2_1_OR_GREATER
    System.IO.Pipelines.PipeReader? Reader => null;
#endif
}
