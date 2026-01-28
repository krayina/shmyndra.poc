using Mavlink.Dialects;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mavlink;

public sealed class MavlinkClient
{
    private readonly Stream _stream;
    private readonly IMavlinkDialect _dialect;
    private readonly MavlinkSigner? _signer;
    private byte _sequence;
    private readonly byte _systemId;
    private readonly byte _componentId;

    private MavlinkClient() { }

    public MavlinkClient(Stream stream, byte systemId, byte componentId, params IMavlinkDialect[] dialect)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _systemId = systemId;
        _componentId = componentId;
        _dialect = PrepareDialectOrThrow(dialect);
    }

    public MavlinkClient(Stream stream, byte systemId, byte componentId, MavlinkSigner signer, params IMavlinkDialect[] dialect)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _systemId = systemId;
        _componentId = componentId;
        _dialect = PrepareDialectOrThrow(dialect);

        _signer = signer ?? throw new ArgumentNullException(nameof(stream));
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default) where T : IMavlinkMessage
    {
        var info = _dialect.GetInfo(typeof(T));
        if (info == null)
        {
            throw new ArgumentException($"Type {typeof(T).Name} not registered");
        }

        var typedInfo = (IMavlinkMessageInfo<T>)info;
        return SerializeAndSendTyped(message, typedInfo, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var info = _dialect.GetInfo(message.GetType());
        if (info == null)
        {
            throw new ArgumentException($"Message type {message.GetType().Name} is not supported by the linked dialects.");
        }

        return SerializeAndSendUntyped(message, info, ct);
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private ValueTask SerializeAndSendTyped<T>(T message, IMavlinkMessageInfo<T> info, CancellationToken ct) where T : IMavlinkMessage
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_V2 + 20 + 13);

        try
        {
            int length = MavlinkPacketSerializer.SerializeV2(
                message,
                info,
                _sequence++,
                _systemId,
                _componentId,
                buffer,
                _signer);
            return SendBufferAsync(buffer, length, ct);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private ValueTask SerializeAndSendUntyped(IMavlinkMessage message, IMavlinkMessageInfo info, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_V2 + 20);
        try
        {
            int length = MavlinkPacketSerializer.SerializeV2(
                message,
                info,
                _sequence++,
                _systemId,
                _componentId,
                buffer,
                _signer);
            return SendBufferAsync(buffer, length, ct);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    private async ValueTask SendBufferAsync(byte[] buffer, int length, CancellationToken ct)
    {
        try
        {
#if NETSTANDARD2_1_OR_GREATER
            await _stream.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
#else
            await _stream.WriteAsync(buffer, 0, length, ct).ConfigureAwait(false);
#endif
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IMavlinkDialect PrepareDialectOrThrow(IMavlinkDialect[] dialects)
    {
        if (dialects == null || dialects.Length == 0)
        {
            throw new ArgumentException("At least one dialect is required.", nameof(dialects));
        }

        if (dialects.Length == 1)
        {
            return dialects[0] ?? throw new ArgumentException("Dialect cannot be null");
        }

        return new MavlinkDialectCompositor(dialects);
    }
}
