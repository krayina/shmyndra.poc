namespace Mavlink;

public interface IMavlinkPortProvider
{
    bool CanRecreatePort { get; }
    ValueTask<IMavlinkPort> CreatePortAsync(CancellationToken ct);
}
