using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Mavlink.Transport;

public sealed class MavlinkStreamPort : IMavlinkPort
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly IDisposable? _owner;
    private readonly Action? _cancelHook;
    private int _disposed;

#if NETSTANDARD2_1_OR_GREATER
    public System.IO.Pipelines.PipeReader? Reader { get; }
#endif

    public MavlinkStreamPort(
        Stream stream,
        bool leaveOpen = false,
        IDisposable? owner = null,
        Action? cancelHook = null,
        bool exposeReader = true)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
        _owner = owner;
        _cancelHook = cancelHook;

#if NETSTANDARD2_1_OR_GREATER
        if (exposeReader)
        {
            // leaveOpen: true intentionally — this port owns disposal of the
            // stream and the owner itself; the reader must never dispose the
            // stream a second time behind our back.
            Reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
        }
#endif
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        ThrowIfDisposed();

        using var registration = RegisterCancelHook(ct);

        try
        {
#if NETSTANDARD2_1_OR_GREATER
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
#else
            if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> segment)
                && segment.Array is not null)
            {
                return await _stream.ReadAsync(segment.Array, segment.Offset, segment.Count, ct)
                    .ConfigureAwait(false);
            }

            var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int read = await _stream.ReadAsync(temp, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read > 0)
                {
                    temp.AsSpan(0, read).CopyTo(buffer.Span);
                }
                return read;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
#endif
        }
        catch (Exception ex) when (IsTransportError(ex))
        {
            throw Translate(ex, ct, "read");
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ThrowIfDisposed();

        using var registration = RegisterCancelHook(ct);

        try
        {
#if NETSTANDARD2_1_OR_GREATER
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
#else
            if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment)
                && segment.Array is not null)
            {
                await _stream.WriteAsync(segment.Array, segment.Offset, segment.Count, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                var array = data.ToArray();
                await _stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
            }
#endif
        }
        catch (Exception ex) when (IsTransportError(ex))
        {
            throw Translate(ex, ct, "write");
        }
    }

    private CancellationTokenRegistration RegisterCancelHook(CancellationToken ct)
    {
        if (_cancelHook is null)
        {
            return default;
        }
        else
        {
            return ct.Register(static state =>
            {
                try
                {
                    ((Action)state!)();
                }
                catch
                {
                    // Suppress cancel hook faults
                }
            }, _cancelHook);
        }
    }

    private static bool IsTransportError(Exception ex)
    {
        return ex is IOException
            or ObjectDisposedException
            or InvalidOperationException
            or UnauthorizedAccessException
            or NotSupportedException
            or SocketException;
    }

    private Exception Translate(Exception ex, CancellationToken ct, string op)
    {
        if (ct.IsCancellationRequested)
        {
            return new OperationCanceledException(ct);
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return new ObjectDisposedException(nameof(MavlinkStreamPort), ex);
        }

        return new MavlinkConnectionException($"Port error during {op}.", ex);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkStreamPort));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

#if NETSTANDARD2_1_OR_GREATER
        try
        {
            Reader?.Complete();
        }
        catch
        {
            // Suppress reader completion faults
        }
#endif

        if (_leaveOpen)
        {
            return;
        }

        try
        {
            _stream.Dispose();
        }
        catch
        {
            // Suppress stream disposal faults
        }

        try
        {
            _owner?.Dispose();
        }
        catch
        {
            // Suppress owner disposal faults
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

#if NETSTANDARD2_1_OR_GREATER
        try
        {
            if (Reader is { } reader)
            {
                await reader.CompleteAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Suppress reader completion faults
        }
#endif

        if (_leaveOpen)
        {
            return;
        }

        try
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
        catch
        {
            // Suppress stream disposal faults
        }

        try
        {
            if (_owner is IAsyncDisposable asyncOwner)
            {
                await asyncOwner.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _owner?.Dispose();
            }
        }
        catch
        {
            // Suppress owner disposal faults
        }
    }
}
