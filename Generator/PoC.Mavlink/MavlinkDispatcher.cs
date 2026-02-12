using System.Threading.Channels;

namespace Mavlink;

internal sealed class MavlinkDispatcher : IDisposable
{
    private readonly MavlinkEventBus _eventBus;
    private readonly Channel<MavlinkReceivedPacket> _channel;
    private Task? _loopTask;

    public MavlinkDispatcher(MavlinkEventBus eventBus, int channelCapacity = 256)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _channel = Channel.CreateBounded<MavlinkReceivedPacket>(
            new BoundedChannelOptions(channelCapacity)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });
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
            catch { }
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
