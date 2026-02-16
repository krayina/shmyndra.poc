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

    public void Start()
    {
        if (_loopTask != null)
        {
            throw new InvalidOperationException("Already started.");
        }

        _loopTask = Task.Run(DispatchLoopAsync);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public async Task DrainAsync()
    {
        if (_loopTask != null)
        {
            await _loopTask.ConfigureAwait(false);
        }
    }

    private async Task DispatchLoopAsync()
    {
        await foreach (var packet in _channel.Reader
                           .ReadAllAsync()
                           .ConfigureAwait(false))
        {
            _eventBus.Publish(in packet);
        }
    }

    public void Dispose()
    {
        Complete();
    }
}
