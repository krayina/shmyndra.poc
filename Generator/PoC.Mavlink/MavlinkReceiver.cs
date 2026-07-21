using Mavlink.Dialects;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkReceiver : IDisposable, IAsyncDisposable
{
    private readonly System.IO.Pipelines.PipeReader _input;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly IMavlinkPacketListener _listener;
    private readonly IMavlinkParserErrorListener _errorListener;
    private readonly IMavlinkRawFrameListener? _frameListener;
    private readonly IMavlinkFrameVerifier? _verifier;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private int _disposed;
#if !NETSTANDARD2_1_OR_GREATER
    private readonly MavlinkFrameReader _framer = new();
#endif

    public MavlinkReceiver(
        System.IO.Pipelines.PipeReader input,
        IMavlinkDialect dialect,
        MavlinkDiagnostics diagnostics,
        IMavlinkPacketListener listener,
        IMavlinkParserErrorListener errorListener,
        IMavlinkRawFrameListener? frameListener = null,
        IMavlinkFrameVerifier? verifier = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _errorListener = errorListener ?? throw new ArgumentNullException(nameof(errorListener));
        _frameListener = frameListener;
        _verifier = verifier;
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
        catch (Exception ex)
        {
            if (Volatile.Read(ref _disposed) == 0 && !ct.IsCancellationRequested)
            {
                _errorListener.OnReceiverFault(ex);
            }
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    private static readonly byte[] _magics = { MavlinkConstants.MAGIC_V1, MavlinkConstants.MAGIC_V2 };
    private SequencePosition EnqueueFramesFromSequence(ReadOnlySequence<byte> sequence)
    {
        var reader = new SequenceReader<byte>(sequence);
        // 10 + 255 + 2 + 13 = 280;
        Span<byte> scratch = stackalloc byte[MavlinkConstants.MAX_FRAME_LENGTH];

        while (true)
        {
            if (!reader.TryAdvanceToAny(_magics, advancePastDelimiter: false))
            {
                reader.Advance(reader.Remaining);
                break;
            }

            var peek = reader;
            if (peek.Remaining < 3) // magic + len + (for V2) incompat
            {
                break;
            }

            peek.TryRead(out byte magicByte);
            peek.TryRead(out byte lenByte);

            var version = magicByte == MavlinkConstants.MAGIC_V2
                ? MavlinkPacketVersion.V2
                : MavlinkPacketVersion.V1;

            int fullLength = (version == MavlinkPacketVersion.V2 ? 10 : 6) + lenByte + 2;

            if (version == MavlinkPacketVersion.V2)
            {
                peek.TryRead(out byte incompatFlags);

                // Spec: unknown incompatibility bits => the frame is incompatible, so we drop it.
                if ((incompatFlags & ~(byte)MavlinkIncompatFlags.Signed) != 0)
                {
                    _errorListener.OnParserError(MavlinkDeserializeResult.UnsupportedIncompatFlags);
                    reader.Advance(1);
                    continue;
                }

                if ((incompatFlags & (byte)MavlinkIncompatFlags.Signed) != 0)
                {
                    fullLength += 13;
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
                var dst = scratch.Slice(0, fullLength);
                packetSlice.CopyTo(dst);
                parsed = ParseAndDispatchFrame(dst, version);
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

        NotifyFrameReceived(frame);

        if (_verifier != null && !_verifier.Verify(frame, in packet))
        {
            return true;
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

                NotifyFrameReceived(frame);

                if (_verifier != null && !_verifier.Verify(frame, in packet))
                {
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

    private void NotifyFrameReceived(ReadOnlySpan<byte> frame)
    {
        if (_frameListener is null)
        {
            return;
        }

        try
        {
            _frameListener.OnFrame(MavlinkFrameDirection.Received, frame);
        }
        catch (Exception ex)
        {
            // User listener faults must not kill the read loop.
            _diagnostics.OnFrameListenerFault(ex);
        }
    }

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

        bool loopExited = true;
        if (_task != null)
        {
            try
            {
                loopExited = _task.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // finishing
            }
        }

        if (loopExited)
        {
            try
            {
                _input.Complete();
            }
            catch
            {
            }
            _cts.Dispose();
        }
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
