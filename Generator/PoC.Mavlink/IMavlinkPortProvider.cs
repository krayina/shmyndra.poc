namespace Mavlink;

public interface IMavlinkPortProvider
{
    ValueTask<IMavlinkPort> CreatePortAsync(CancellationToken ct);

    bool CanReconnect { get; }
}