using System.Net.Sockets;

namespace Mavlink;

public sealed class MavlinkUdpPort : IMavlinkPort
{
    private readonly UdpClient _udp;
    private readonly bool _leaveOpen;
    private int _disposed;

    public MavlinkUdpPort(UdpClient udp, bool leaveOpen = false)
    {
        _udp = udp ?? throw new ArgumentNullException(nameof(udp));
        _leaveOpen = leaveOpen;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        UdpReceiveResult result;

#if NET6_0_OR_GREATER
        result = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
#else
        result = await ReceiveWithCancellationAsync(ct).ConfigureAwait(false);
#endif

        var data = result.Buffer;

        if (data.Length > buffer.Length)
            throw new InvalidOperationException(
                $"Datagram ({data.Length}B) exceeds buffer ({buffer.Length}B)");

        data.CopyTo(buffer);
        return data.Length;
    }

#if !NET6_0_OR_GREATER
    /// <summary>
    /// On platforms without CancellationToken support, cancelling the read
    /// closes the underlying socket. The port becomes unusable after cancellation.
    /// </summary>
    private async Task<UdpReceiveResult> ReceiveWithCancellationAsync(CancellationToken ct)
    {
        var receiveTask = _udp.ReceiveAsync();

        using (ct.Register(() =>{ try { _udp.Client.Close(); } catch { } }))
        {
            try
            {
                return await receiveTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                Volatile.Write(ref _disposed, 1);
                throw new OperationCanceledException(ct);
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                Volatile.Write(ref _disposed, 1);
                throw new OperationCanceledException(ct);
            }
        }
    }
#endif

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

#if NET6_0_OR_GREATER
        await _udp.SendAsync(data, ct).ConfigureAwait(false);
#else
        var array = data.ToArray();
        await _udp.SendAsync(array, array.Length).ConfigureAwait(false);
#endif
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (!_leaveOpen)
        {
            _udp.Dispose();
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
#endif
}