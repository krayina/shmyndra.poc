using Mavlink.Dialects;
using System.Buffers;
using System.IO.Pipelines;

namespace Mavlink;

internal sealed class MavlinkReceiver : IDisposable
{
    private readonly IMavlinkPort _port;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly MavlinkEventBus _eventBus;
    private readonly MavlinkFrameReader _framer;

    private readonly CancellationTokenSource _cts;
    private readonly Task _readTask;
    private int _disposed;

    public event Action? ReadingStopped;

    public MavlinkReceiver(
        IMavlinkPort port,
        IMavlinkDialect dialect,
        MavlinkDispatcher dispatcher,
        MavlinkDiagnostics diagnostics,
        MavlinkEventBus eventBus)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        _framer = new MavlinkFrameReader();
        _cts = new CancellationTokenSource();

        // Запуск потоку читання відбувається автоматично всередині
        _readTask = ReadLoopAsync(_cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
#if NETSTANDARD2_1_OR_GREATER
        if (_port.Reader != null)
        {
            await ReadLoopPipelinesAsync(_port.Reader, ct).ConfigureAwait(false);
            return;
        }
#endif
        await ReadLoopFallbackAsync(ct).ConfigureAwait(false);
    }

#if NETSTANDARD2_1_OR_GREATER
    private async Task ReadLoopPipelinesAsync(PipeReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = EnqueueFramesFromSequence(buffer);
                reader.AdvanceTo(consumed, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            NotifyStopping();
        }
    }

    private SequencePosition EnqueueFramesFromSequence(ReadOnlySequence<byte> sequence)
    {
        var reader = new SequenceReader<byte>(sequence);

        while (true)
        {
            if (!reader.TryAdvanceTo(MavlinkConstants.MAGIC_V2, advancePastDelimiter: false) &&
                !reader.TryAdvanceTo(MavlinkConstants.MAGIC_V1, advancePastDelimiter: false))
            {
                break;
            }

            var peekReader = reader;
            if (peekReader.Remaining < 2) break;

            peekReader.TryRead(out byte magicByte);
            peekReader.TryRead(out byte lenByte);

            MavlinkPacketVersion version = (magicByte == MavlinkConstants.MAGIC_V2)
                ? MavlinkPacketVersion.V2
                : MavlinkPacketVersion.V1;

            int headerLen = (version == MavlinkPacketVersion.V2) ? 10 : 6;
            int fullLength = headerLen + lenByte + 2;

            if (version == MavlinkPacketVersion.V2)
            {
                if (peekReader.TryRead(out byte incompatFlags) && (incompatFlags & 0x01) != 0)
                {
                    fullLength += 13;
                }
            }

            if (reader.Remaining < fullLength) break;

            ReadOnlySequence<byte> packetSlice = reader.Sequence.Slice(reader.Position, fullLength);
            bool isParsedSuccessfully;

            if (packetSlice.IsSingleSegment)
            {
                isParsedSuccessfully = ParseAndDispatchFrame(packetSlice.FirstSpan, version);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[fullLength];
                packetSlice.CopyTo(stackBuffer);
                isParsedSuccessfully = ParseAndDispatchFrame(stackBuffer, version);
            }

            if (isParsedSuccessfully)
            {
                reader.Advance(fullLength);
            }
            else
            {
                reader.Advance(1);
            }
        }

        return reader.Position;
    }

    private bool ParseAndDispatchFrame(ReadOnlySpan<byte> frame, MavlinkPacketVersion version)
    {
        var result = MavlinkPacketParser.TryParse(frame, version, _dialect, out var packet);

        if (result != MavlinkDeserializeResult.Success)
        {
            _diagnostics.OnDeserializeError(result);
            return false;
        }

        _diagnostics.OnReceived();
        _diagnostics.TrackSequence(packet.SenderSystemId, packet.SenderComponentId, packet.Sequence);
        _dispatcher.TryEnqueue(in packet);
        return true;
    }
#endif

    private async Task ReadLoopFallbackAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await _port.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0) break;

                EnqueueFrames(buffer, read);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            NotifyStopping();
        }
    }

    private void EnqueueFrames(byte[] data, int count)
    {
        _framer.Append(data, 0, count);

        while (_framer.TryReadFrame(out int offset, out int length, out var version))
        {
            var frame = _framer.RawBuffer.AsSpan(offset, length);
            var result = MavlinkPacketParser.TryParse(frame, version, _dialect, out var packet);

            if (result != MavlinkDeserializeResult.Success)
            {
                _diagnostics.OnDeserializeError(result);
                continue;
            }

            _diagnostics.OnReceived();
            _diagnostics.TrackSequence(packet.SenderSystemId, packet.SenderComponentId, packet.Sequence);
            _dispatcher.TryEnqueue(in packet);
        }
        _framer.CompactIfNeeded();
    }

    private void NotifyStopping()
    {
        _dispatcher.Complete();
        try
        {
            ReadingStopped?.Invoke();
        }
        catch (Exception ex)
        {
            _eventBus.RaiseError(ex);
        }
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _cts.Cancel();
        try
        {
            await _readTask.ConfigureAwait(false);
        }
        catch { /* ігноруємо помилки скасування при закритті */ }
        finally
        {
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _cts.Cancel();
        try
        {
            _readTask.GetAwaiter().GetResult();
        }
        catch { }
        finally
        {
            _cts.Dispose();
        }
    }
}
