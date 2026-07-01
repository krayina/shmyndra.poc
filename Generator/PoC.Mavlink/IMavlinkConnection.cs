namespace Mavlink;

public interface IMavlinkConnection : IAsyncDisposable
{
    MavlinkConnectionState State { get; }

    event Action<MavlinkConnectionStateChangedEventArgs>? StateChanged;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    // Receiver reads from here. A single Pipe that survives reconnects:
    // while a port is being swapped, ReadAsync simply blocks until the new
    // port starts flowing bytes again. Receiver never notices the blip.
    System.IO.Pipelines.PipeReader Input { get; }

    // Sender writes through here. Throws InvalidOperationException while
    // the state is not Connected (e.g. mid-reconnect).
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}