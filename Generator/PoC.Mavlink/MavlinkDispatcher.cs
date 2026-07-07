using System.Threading.Channels;

namespace Mavlink;

internal sealed class MavlinkDispatcher : IDisposable, IAsyncDisposable
{
    private readonly MavlinkEventBus _eventBus;
    private readonly Channel<MavlinkReceivedPacket> _channel;
    private Task? _loopTask;
    private int _disposed;

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

    private async Task DispatchLoopAsync()
    {
        try
        {
            await foreach (var packet in _channel.Reader
                               .ReadAllAsync()
                               .ConfigureAwait(false))
            {
                _eventBus.Publish(in packet);
            }
        }
        catch
        {
            // Suppress exceptions during dispatch loop
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        Complete();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        Complete();

        if (_loopTask != null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch
            {
                // Suppress exceptions when awaiting the loop task
            }
        }
    }
}
