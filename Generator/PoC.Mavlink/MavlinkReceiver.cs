using Mavlink.Dialects;
using System.Buffers;
using System.IO.Pipelines;

namespace Mavlink;

internal sealed class MavlinkReceiver : IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkDispatcher _dispatcher;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly MavlinkEventBus _eventBus;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;

#if !NETSTANDARD2_1_OR_GREATER
    private readonly MavlinkFrameReader _framer = new();
#endif

    public MavlinkReceiver(IMavlinkConnection connection, IMavlinkDialect dialect,
        MavlinkDispatcher dispatcher, MavlinkDiagnostics diagnostics, MavlinkEventBus eventBus)
        => (_connection, _dialect, _dispatcher, _diagnostics, _eventBus)
           = (connection, dialect, dispatcher, diagnostics, eventBus);

    public void Start()
    {
        if (_task != null) return;
        _task = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var reader = _connection.Input; // Стабільний PipeReader, що переживає реконнекти
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (buffer.IsEmpty && result.IsCompleted) break;

                SequencePosition consumed;
#if NETSTANDARD2_1_OR_GREATER
                consumed = EnqueueFramesFromSequence(buffer);
#else
                consumed = EnqueueFramesFallback(buffer);
#endif
                reader.AdvanceTo(consumed, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _eventBus.RaiseError(ex); }
        finally { await reader.CompleteAsync().ConfigureAwait(false); }
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

            if (isParsedSuccessfully) reader.Advance(fullLength);
            else reader.Advance(1);
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
                    _diagnostics.OnDeserializeError(result);
                    continue;
                }

                _diagnostics.OnReceived();
                _diagnostics.TrackSequence(packet.SenderSystemId, packet.SenderComponentId, packet.Sequence);
                _dispatcher.TryEnqueue(in packet);
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
        _cts.Cancel();
        _cts.Dispose();
    }
}