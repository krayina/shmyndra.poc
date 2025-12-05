using PoC.Mavlink;
using System.Buffers;

namespace Mavlink;

public sealed class MavlinkClient
{
    private readonly Stream _stream;
    private byte _sequence;
    private readonly byte _systemId;
    private readonly byte _componentId;

    public MavlinkClient(Stream stream, byte systemId, byte componentId)
    {
        _stream = stream;
        _systemId = systemId;
        _componentId = componentId;
    }

    public ValueTask SendAsync<T>(T message, CancellationToken ct = default) where T : IMavlinkMessage
    {
        var info = MavlinkInfoCache<T>.Info;

        if (info == null)
        {
            throw new ArgumentException($"Type {typeof(T).Name} not registered");
        }
        return SerializeAndSend(message, info, ct);
    }

    public ValueTask SendAsync(IMavlinkMessage message, CancellationToken ct = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (MavlinkDialectRegistry.TryRoute(this, message, ct, out var task))
        {
            return task;
        }

        // Fallback
        var info = MavlinkDialectRegistry.GetInfo(message.GetType());

        if (info == null)
        {
            throw new ArgumentException($"Unknown message type: {message.GetType().Name}");
        }

        return SerializeAndSend(message, info, ct);
    }

    private ValueTask SerializeAndSend<T>(T message, IMavlinkMessageInfo<T> info, CancellationToken ct) where T : IMavlinkMessage
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MavlinkConstants.MAX_PAYLOAD_V2 + 20);
        try
        {
            int len = MavlinkPacketSerializer.SerializeV2(message, info, _sequence++, _systemId, _componentId, buffer);

            return _stream.WriteAsync(buffer.AsMemory(0, len), ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
