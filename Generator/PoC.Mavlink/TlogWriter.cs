using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Threading.Channels;

namespace Mavlink.Transport;

public sealed class TlogWriter : IAsyncDisposable, IDisposable
{
    private const long UnixEpochTicks = 621355968000000000L;

    private readonly Channel<Entry> _queue;
    private readonly FileStream _file;
    private readonly Task _writeLoop;
    private long _dropped;
    private int _disposed;

    private readonly struct Entry
    {
        public Entry(long timestampUs, byte[] buffer, int length)
        {
            TimestampUs = timestampUs;
            Buffer = buffer;
            Length = length;
        }

        public long TimestampUs { get; }
        public byte[] Buffer { get; }
        public int Length { get; }
    }

    public TlogWriter(string path, int queueCapacity = 1024)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        _file = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        _queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _writeLoop = Task.Run(WriteLoopAsync);
    }

    public long DroppedChunks => Interlocked.Read(ref _dropped);

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        long tsUs = (DateTime.UtcNow.Ticks - UnixEpochTicks) / 10;

        var rented = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(rented);

        if (!_queue.Writer.TryWrite(new Entry(tsUs, rented, data.Length)))
        {
            ArrayPool<byte>.Shared.Return(rented);
            Interlocked.Increment(ref _dropped);
        }
    }

    private async Task WriteLoopAsync()
    {
        var tsBuf = new byte[8];

        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var e))
                {
                    try
                    {
                        BinaryPrimitives.WriteInt64BigEndian(tsBuf, e.TimestampUs);
                        await _file.WriteAsync(tsBuf, 0, 8).ConfigureAwait(false);
                        await _file.WriteAsync(e.Buffer, 0, e.Length).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(e.Buffer);
                    }
                }
                try
                {
                    await _file.FlushAsync().ConfigureAwait(false);
                }
                catch
                {
                    // IO fault: the next WriteAsync call will throw it anyway.
                }
            }
        }
        catch (Exception ex)
        {
            _queue.Writer.TryComplete(ex);
            while (_queue.Reader.TryRead(out var e))
            {
                ArrayPool<byte>.Shared.Return(e.Buffer);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();
        try
        {
            await _writeLoop.ConfigureAwait(false);
        }
        catch { }

        DrainQueue();

        try
        {
            await _file.FlushAsync().ConfigureAwait(false);
        } catch { }
        _file.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();

        try
        {
            _writeLoop.Wait(TimeSpan.FromSeconds(5));
        } catch { }

        DrainQueue();

        try
        {
            _file.Flush();
        } catch { }
        _file.Dispose();
    }

    private void DrainQueue()
    {
        while (_queue.Reader.TryRead(out var e))
        {
            ArrayPool<byte>.Shared.Return(e.Buffer);
        }
    }
}
