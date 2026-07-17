using System.Net;
using System.Net.Sockets;

namespace Mavlink.Transport;

/// <summary>
/// UDP port in listen (server) mode: bound to a local port, replies to the
/// most recent remote sender. Until a peer is discovered, outgoing frames
/// are silently dropped (there is nowhere to send them yet) — this matches
/// the behavior of most GCS implementations.
/// </summary>
public sealed class MavlinkUdpListenPort : IMavlinkPort
{
    private readonly UdpClient _udp;
    private volatile IPEndPoint? _remote;
    private int _disposed;

#if NETSTANDARD2_1_OR_GREATER
        public System.IO.Pipelines.PipeReader? Reader => null;
#endif

    public MavlinkUdpListenPort(UdpClient udp)
    {
        _udp = udp ?? throw new ArgumentNullException(nameof(udp));
    }

    /// <summary>
    /// The last remote peer a datagram was received from, if any.
    /// </summary>
    public IPEndPoint? RemoteEndPoint => _remote;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        ThrowIfDisposed();

        try
        {
            UdpReceiveResult result;

#if NET6_0_OR_GREATER
            result = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
#else
            result = await ReceiveWithCancellationAsync(ct).ConfigureAwait(false);
#endif

            _remote = result.RemoteEndPoint;

            var data = result.Buffer;
            if (data.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    $"Datagram ({data.Length}B) exceeds buffer ({buffer.Length}B)");
            }

            data.CopyTo(buffer);
            return data.Length;
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MavlinkUdpListenPort), ex);
            }

            throw new MavlinkConnectionException("Port error during read.", ex);
        }
    }

#if !NET6_0_OR_GREATER
    private async Task<UdpReceiveResult> ReceiveWithCancellationAsync(CancellationToken ct)
    {
        var receiveTask = _udp.ReceiveAsync();
 
        using (ct.Register(static state =>
        {
            try
            {
                ((UdpClient)state!).Client.Close();
            }
            catch
            {
                // Suppress exception during socket close
            }
        }, _udp))
        {
            try
            {
                return await receiveTask.ConfigureAwait(false);
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }
#endif

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ThrowIfDisposed();

        var remote = _remote;
        if (remote is null)
        {
            // No peer discovered yet — drop. See class remarks.
            return;
        }

        try
        {
#if NET6_0_OR_GREATER
            await _udp.SendAsync(data, remote, ct).ConfigureAwait(false);
#else
            var array = data.ToArray();
            await _udp.SendAsync(array, array.Length, remote).ConfigureAwait(false);
#endif
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MavlinkUdpListenPort), ex);
            }

            throw new MavlinkConnectionException("Port error during write.", ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkUdpListenPort));
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
            _udp.Client?.Close();
        }
        catch
        {
            // Suppress exception during client close
        }

        try
        {
            _udp.Dispose();
        }
        catch
        {
            // Suppress exception during disposal
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
