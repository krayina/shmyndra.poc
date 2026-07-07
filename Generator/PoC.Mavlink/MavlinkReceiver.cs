using Mavlink.Dialects;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkReceiver : IDisposable, IAsyncDisposable
{
    private readonly System.IO.Pipelines.PipeReader _input;
    private readonly IMavlinkDialect _dialect;
    private readonly IMavlinkPacketListener _listener;
    private readonly IMavlinkParserErrorListener _errorListener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private int _disposed;
#if !NETSTANDARD2_1_OR_GREATER
    private readonly MavlinkFrameReader _framer = new();
#endif

    public MavlinkReceiver(
        System.IO.Pipelines.PipeReader input,
        IMavlinkDialect dialect,
        IMavlinkPacketListener listener,
        IMavlinkParserErrorListener errorListener)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _errorListener = errorListener ?? throw new ArgumentNullException(nameof(errorListener));
    }

    public void Start()
    {
        if (_task != null)
        {
            return;
        }

        _task = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _input.ReadAsync(ct).ConfigureAwait(false);
                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;
                if (buffer.IsEmpty && result.IsCompleted)
                {
                    break;
                }

                SequencePosition consumed;
#if NETSTANDARD2_1_OR_GREATER                                
                consumed = EnqueueFramesFromSequence(buffer);
#else
                consumed = EnqueueFramesFallback(buffer);
#endif
                _input.AdvanceTo(consumed, buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal termination upon cancellation
        }
        catch (Exception)
        {
            if (Volatile.Read(ref _disposed) == 0 && !ct.IsCancellationRequested)
            {
                _errorListener.OnParserError(MavlinkDeserializeResult.UnknownMagicByte);
            }
        }
    }

#if NETSTANDARD2_1_OR_GREATER        
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
            if (peekReader.Remaining < 2)
            {
                break;
            }

            peekReader.TryRead(out byte magicByte);
            peekReader.TryRead(out byte lenByte);

            var version = magicByte == MavlinkConstants.MAGIC_V2
                ? MavlinkPacketVersion.V2
                : MavlinkPacketVersion.V1;

            int headerLen = version == MavlinkPacketVersion.V2 ? 10 : 6;
            int fullLength = headerLen + lenByte + 2; // +2 CRC

            if (version == MavlinkPacketVersion.V2)
            {
                if (peekReader.TryRead(out byte incompatFlags) && (incompatFlags & 0x01) != 0)
                {
                    fullLength += 13; // Add 13 bytes for the signature
                }
            }

            if (reader.Remaining < fullLength)
            {
                break;
            }

            var packetSlice = reader.Sequence.Slice(reader.Position, fullLength);
            bool parsed;

            if (packetSlice.IsSingleSegment)
            {
                parsed = ParseAndDispatchFrame(packetSlice.FirstSpan, version);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[fullLength];
                packetSlice.CopyTo(stackBuffer);
                parsed = ParseAndDispatchFrame(stackBuffer, version);
            }

            reader.Advance(parsed ? fullLength : 1);
        }

        return reader.Position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ParseAndDispatchFrame(ReadOnlySpan<byte> frame, MavlinkPacketVersion version)
    {
        var result = MavlinkPacketParser.TryParse(frame, version, _dialect, out var packet);

        if (result != MavlinkDeserializeResult.Success)
        {
            _errorListener.OnParserError(result);
            return false;
        }

        _listener.OnPacketReceived(in packet);
        return true;
    }
#else
    private SequencePosition EnqueueFramesFallback(ReadOnlySequence<byte> sequence)
    {
        int totalLength = (int)sequence.Length;
        var pool = ArrayPool<byte>.Shared;
        byte[] array = pool.Rent(totalLength);

        try
        {
            sequence.CopyTo(array);
            _framer.Append(array, 0, totalLength);

            while (_framer.TryReadFrame(out int offset, out int length, out var version))
            {
                var frame = _framer.RawBuffer.AsSpan(offset, length);
                var result = MavlinkPacketParser.TryParse(frame, version, _dialect, out var packet);

                if (result != MavlinkDeserializeResult.Success)
                {
                    _errorListener.OnParserError(result);
                    continue;
                }

                _listener.OnPacketReceived(in packet);
            }

            _framer.CompactIfNeeded();
        }
        finally
        {
            pool.Return(array);
        }

        return sequence.End;
    }
#endif

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _input.CancelPendingRead();
        }
        catch
        {
            // Suppress exceptions during pending read cancellation
        }

        try
        {
            _input.Complete();
        }
        catch
        {
            // Suppress completion exceptions
        }

        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _input.CancelPendingRead();
        }
        catch
        {
            // Suppress exceptions during pending read cancellation
        }

        if (_task != null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch
            {
                // Suppress exceptions from the running task
            }
        }

        try
        {
            _input.Complete();
        }
        catch
        {
            // Suppress completion exceptions
        }

        _cts.Dispose();
    }
}
