namespace Mavlink;

public interface IMavlinkPortProvider
{
    bool CanRecreatePort { get; }

    bool CanWrite { get; }

    ValueTask<IMavlinkPort> CreatePortAsync(CancellationToken ct);
}
