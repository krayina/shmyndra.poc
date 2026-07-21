using System.Runtime.CompilerServices;

namespace Mavlink;

internal sealed class MavlinkSender : IDisposable, IAsyncDisposable
{
    private readonly IMavlinkConnection _connection;
    private readonly MavlinkDiagnostics _diagnostics;
    private readonly MavlinkSigner? _signer;
    private readonly IMavlinkRawFrameListener? _frameListener;
    private readonly byte[] _buffer = new byte[MavlinkConstants.MAX_PAYLOAD_ARRAY_POOL_SIZE];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _disposed;

    public MavlinkSender(
        IMavlinkConnection connection,
        MavlinkDiagnostics diagnostics,
        MavlinkSigner? signer = null,
        IMavlinkRawFrameListener? frameListener = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _signer = signer;
        _frameListener = frameListener;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<int> SendFrameAsync<T>(
        T message,
        IMavlinkMessageInfo info,
        byte sequence,
        byte systemId,
        byte componentId,
        MavlinkPacketVersion version,
        CancellationToken ct)
        where T : IMavlinkMessage
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            int len = MavlinkSerializer.Serialize(
                message, info, sequence, systemId, componentId, _buffer, version, _signer);

            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
            NotifyFrameSent(len);
            return len;
        }
        finally
        {
            _gate.Release();
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<int> SendFrameAsync(
        IMavlinkMessage message,
        IMavlinkMessageInfo info,
        byte sequence,
        byte systemId,
        byte componentId,
        MavlinkPacketVersion version,
        CancellationToken ct)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            int len = MavlinkSerializer.Serialize(
                message, info, sequence, systemId, componentId, _buffer, version, _signer);

            await _connection.WriteAsync(_buffer.AsMemory(0, len), ct).ConfigureAwait(false);
            NotifyFrameSent(len);
            return len;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void NotifyFrameSent(int len)
    {
        if (_frameListener is null)
        {
            return;
        }

        try
        {
            _frameListener.OnFrame(MavlinkFrameDirection.Sent, _buffer.AsSpan(0, len));
        }
        catch (Exception ex)
        {
            // User listener faults must not fail the send path.
            _diagnostics.OnFrameListenerFault(ex);
        }
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

        if (_gate.Wait(TimeSpan.FromSeconds(5)))
        {
            _gate.Release();
        }
        _gate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
    }
}
