namespace Mavlink;

public interface IMavlinkConnection : IAsyncDisposable
{
    System.IO.Pipelines.PipeReader Input { get; }
    MavlinkConnectionState State { get; }

    event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged;

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    void Abort();
}