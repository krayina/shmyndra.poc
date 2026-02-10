using System.Threading.Channels;

namespace Mavlink;

internal sealed class MavlinkDispatcher : IDisposable
{
    private readonly MavlinkEventBus _eventBus;

    private readonly Channel<MavlinkReceivedPacket> _channel =
        Channel.CreateBounded<MavlinkReceivedPacket>(
            new BoundedChannelOptions(256)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

    private Task? _loopTask;

    public MavlinkDispatcher(MavlinkEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public bool TryEnqueue(in MavlinkReceivedPacket packet)
    {
        return _channel.Writer.TryWrite(packet);
    }

    public void Start(CancellationToken ct)
    {
        if (_loopTask != null)
        {
            throw new InvalidOperationException("Already started.");
        }

        _loopTask = Task.Run(() => DispatchLoopAsync(ct));
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public async Task DrainAsync()
    {
        if (_loopTask != null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch { /* OperationCanceledException expected */ }
        }
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var packet in _channel.Reader
                               .ReadAllAsync(ct)
                               .ConfigureAwait(false))
            {
                _eventBus.Publish(in packet);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        Complete();
    }
}