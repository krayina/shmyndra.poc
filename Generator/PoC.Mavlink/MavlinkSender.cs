using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkSender : IDisposable
{
    private readonly byte _systemId;
    private readonly byte _componentId;
    private int _sequence;
    private readonly IMavlinkPort _port;
    private readonly byte[] _sendBuffer = new byte[MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE];
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly MavlinkSigner? _signer;

    public MavlinkSender(
        IMavlinkPort port,
        byte systemId,
        byte componentId,
        MavlinkSigner? signer)
    {
        _port = port;
        _systemId = systemId;
        _componentId = componentId;
        _signer = signer;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync<T>(
        T message, IMavlinkMessageInfo info,
        MavlinkPacketVersion version,
        CancellationToken ct)
        where T : IMavlinkMessage
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte seq = NextSequence();
            int length = MavlinkSerializer.Serialize(
                message, info, seq, _systemId, _componentId,
                _sendBuffer, version, _signer);

            await _port.WriteAsync(_sendBuffer.AsMemory(0, length), ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync(
        IMavlinkMessage message,
        IMavlinkMessageInfo info,
        MavlinkPacketVersion version,
        CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte seq = NextSequence();
            int length = MavlinkSerializer.Serialize(
                message, info, seq, _systemId, _componentId,
                _sendBuffer, version, _signer);

            await _port.WriteAsync(_sendBuffer.AsMemory(0, length), ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private byte NextSequence() => (byte)Interlocked.Increment(ref _sequence);

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}
