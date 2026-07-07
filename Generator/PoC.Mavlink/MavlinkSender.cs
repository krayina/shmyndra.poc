using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkSender : IDisposable, IAsyncDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly byte _systemId;
    private readonly byte _componentId;
    private readonly byte[] _buffer = new byte[MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MavlinkSigner? _signer;
    private int _sequence;
    private int _disposed;

    public MavlinkSender(IMavlinkConnection connection, byte systemId, byte componentId, MavlinkSigner? signer)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _systemId = systemId;
        _componentId = componentId;
        _signer = signer;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync<T>(T message, IMavlinkMessageInfo info, MavlinkPacketVersion version, CancellationToken ct)
        where T : IMavlinkMessage
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            byte seq = NextSequence();
            int len = MavlinkSerializer.Serialize(message, info, seq, _systemId, _componentId, _buffer, version, _signer);

            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _gate.Release();
            }
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask SendAsync(IMavlinkMessage message, IMavlinkMessageInfo info, MavlinkPacketVersion version, CancellationToken ct)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            byte seq = NextSequence();
            int len = MavlinkSerializer.Serialize(message, info, seq, _systemId, _componentId, _buffer, version, _signer);

            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _gate.Release();
            }
        }
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private byte NextSequence()
    {
        return (byte)Interlocked.Increment(ref _sequence);
    }

#if NETSTANDARD2_1_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MavlinkSender));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _gate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
