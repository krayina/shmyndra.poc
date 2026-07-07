using System.Net.Sockets;

namespace Mavlink;

public sealed class MavlinkUdpPort : IMavlinkPort, IAsyncDisposable, IDisposable
{
    private readonly UdpClient _udp;
    private readonly bool _leaveOpen;
    private int _disposed;

#if NETSTANDARD2_1_OR_GREATER
    public System.IO.Pipelines.PipeReader? Reader => null;
#endif

    public MavlinkUdpPort(UdpClient udp, bool leaveOpen = false)
    {
        _udp = udp ?? throw new ArgumentNullException(nameof(udp));
        _leaveOpen = leaveOpen;
    }

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
                throw new ObjectDisposedException(nameof(MavlinkUdpPort), ex);
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

        try
        {
#if NET6_0_OR_GREATER
            await _udp.SendAsync(data, ct).ConfigureAwait(false);
#else
            var array = data.ToArray();
            await _udp.SendAsync(array, array.Length).ConfigureAwait(false);
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
                throw new ObjectDisposedException(nameof(MavlinkUdpPort), ex);
            }

            throw new MavlinkConnectionException("Port error during write.", ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkUdpPort));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        if (!_leaveOpen)
        {
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
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return default;
        }

        if (!_leaveOpen)
        {
#if NET6_0_OR_GREATER
            try
            {
                _udp.Dispose();
            }
            catch
            {
                // Suppress exception during disposal
            }
#else
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
#endif
        }

        return default;
    }
}
