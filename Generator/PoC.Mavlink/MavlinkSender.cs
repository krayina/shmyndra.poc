using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkSender : IDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly byte _systemId, _componentId;
    private readonly byte[] _buffer = new byte[MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MavlinkSigner? _signer;
    private int _sequence;

    public MavlinkSender(IMavlinkConnection connection, byte systemId, byte componentId, MavlinkSigner? signer)
        => (_connection, _systemId, _componentId, _signer) = (connection, systemId, componentId, signer);

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync<T>(T message, IMavlinkMessageInfo info,
        MavlinkPacketVersion version, CancellationToken ct) where T : IMavlinkMessage
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte seq = NextSequence();
            int len = MavlinkSerializer.Serialize(message, info, seq,
                _systemId, _componentId, _buffer, version, _signer);
            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync(IMavlinkMessage message, IMavlinkMessageInfo info,
        MavlinkPacketVersion version, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte seq = NextSequence();
            int len = MavlinkSerializer.Serialize(message, info, seq,
                _systemId, _componentId, _buffer, version, _signer);
            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private byte NextSequence() => (byte)Interlocked.Increment(ref _sequence);

    public void Dispose() => _gate.Dispose();
}
