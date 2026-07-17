using System.Net.WebSockets;
using System.Runtime.InteropServices;

namespace Mavlink.Transport;

/// <summary>
/// Port over a connected <see cref="ClientWebSocket"/>. A Close frame from
/// the server is surfaced as EOF (read returns 0), which lets the channel's
/// reconnect loop take over.
/// </summary>
public sealed class MavlinkWebSocketPort : IMavlinkPort
{
    private readonly ClientWebSocket _ws;
    private int _disposed;

#if NETSTANDARD2_1_OR_GREATER
    public System.IO.Pipelines.PipeReader? Reader => null;
#endif

    public MavlinkWebSocketPort(ClientWebSocket ws)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));

        if (ws.State != WebSocketState.Open)
        {
            throw new ArgumentException("WebSocket must be in the Open state.", nameof(ws));
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        ThrowIfDisposed();

        try
        {
#if NETSTANDARD2_1_OR_GREATER
            var result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
 
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0; // EOF → reconnect loop
            }
 
            return result.Count;
#else
            if (!MemoryMarshal.TryGetArray(
                    (ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> segment)
                || segment.Array is null)
            {
                segment = new ArraySegment<byte>(new byte[buffer.Length]);
            }

            var result = await _ws.ReceiveAsync(segment, ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0; // EOF → reconnect loop
            }

            if (!MemoryMarshal.TryGetArray(
                    (ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> original)
                || !ReferenceEquals(original.Array, segment.Array))
            {
                segment.Array.AsMemory(0, result.Count).CopyTo(buffer);
            }

            return result.Count;
#endif
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            throw Translate(ex, ct, "read");
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ThrowIfDisposed();

        try
        {
#if NETSTANDARD2_1_OR_GREATER
            await _ws.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, ct)
                .ConfigureAwait(false);
#else
            if (!MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
                || segment.Array is null)
            {
                segment = new ArraySegment<byte>(data.ToArray());
            }

            await _ws.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage: true, ct)
                .ConfigureAwait(false);
#endif
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            throw Translate(ex, ct, "write");
        }
    }

    private Exception Translate(Exception ex, CancellationToken ct, string op)
    {
        if (ct.IsCancellationRequested)
        {
            return new OperationCanceledException(ct);
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return new ObjectDisposedException(nameof(MavlinkWebSocketPort), ex);
        }

        return new MavlinkConnectionException($"Port error during {op}.", ex);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkWebSocketPort));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _ws.Abort();
        }
        catch
        {
            // Suppress exception during abort
        }

        try
        {
            _ws.Dispose();
        }
        catch
        {
            // Suppress exception during disposal
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        // Best-effort graceful close with a short cap, then hard dispose.
        if (_ws.State == WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            try
            {
                await _ws.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Suppress exception during graceful close
            }
        }

        try
        {
            _ws.Dispose();
        }
        catch
        {
            // Suppress exception during disposal
        }
    }
}
