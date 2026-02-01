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
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

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
        return SendGuardedAsync(message, info, ct);
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

        return SendGuardedAsync(message, info, ct);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask SendGuardedAsync<T>(T message, IMavlinkMessageInfo info, CancellationToken ct)
        where T : IMavlinkMessage
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);

        byte[]? buffer = null;

        try
        {
            buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE);
            int length;

            if (info is IMavlinkMessageInfo<T> typedInfo)
            {
                length = MavlinkPacketSerializer.SerializeV2(
                    message, typedInfo, _sequence++, _systemId,
                    _componentId, buffer, _signer);
            }
            else
            {
                length = MavlinkPacketSerializer.SerializeV2(
                    message, info, _sequence++, _systemId,
                    _componentId, buffer,_signer);
            }

#if NETSTANDARD2_1_OR_GREATER
            await _stream.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
#else
            await _stream.WriteAsync(buffer, 0, length, ct).ConfigureAwait(false);
#endif
        }
        finally
        {
            if (buffer != null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            _sendLock.Release();
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
