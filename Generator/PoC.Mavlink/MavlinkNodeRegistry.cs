using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Mavlink.Routing;

internal sealed class MavlinkNodeRegistry : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly long _timeoutTicks;
    private readonly ConcurrentDictionary<byte, MavlinkSystemView> _systems = new();
    private readonly Func<byte, MavlinkSystemView> _systemFactory;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _scanTask;
    private int _disposed;

    public MavlinkNodeRegistry(MavlinkEventBus eventBus, TimeSpan systemTimeout)
    {
        if (systemTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(systemTimeout));
        }

        if (eventBus is null)
        {
            throw new ArgumentNullException(nameof(eventBus));
        }

        _systemFactory = id => new MavlinkSystemView(id, eventBus);
        _timeoutTicks = systemTimeout.Ticks;
        _scanTask = Task.Run(() => ScanLoopAsync(_cts.Token));
    }

    public event Action<MavlinkSystemView>? SystemDiscovered;

    public IReadOnlyCollection<MavlinkSystemView> Systems
        => (IReadOnlyCollection<MavlinkSystemView>)_systems.Values;

    public MavlinkSystemView GetSystem(byte systemId)
        => _systems.GetOrAdd(systemId, _systemFactory);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnPacket(in MavlinkReceivedPacket packet)
    {
        GetSystem(packet.SenderSystemId).OnPacket(in packet, DateTime.UtcNow.Ticks);
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ScanInterval, ct).ConfigureAwait(false);

                var now = DateTime.UtcNow.Ticks;

                foreach (var kvp in _systems)
                {
                    var view = kvp.Value;
                    bool discovered = view.Scan(now, _timeoutTicks);

                    if (discovered)
                    {
                        try
                        {
                            SystemDiscovered?.Invoke(view);
                        }
                        catch
                        {
                            // Listener faults must not kill the scan loop.
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Suppress cancellation faults.
        }

        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Suppress cancellation faults.
        }

        try
        {
            await _scanTask.ConfigureAwait(false);
        }
        catch
        {
            // Scan loop faults are already contained; nothing to surface here.
        }

        _cts.Dispose();
    }
}
