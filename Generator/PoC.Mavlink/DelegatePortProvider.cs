namespace Mavlink;

public sealed class DelegatePortProvider : IMavlinkPortProvider
{
    private readonly Func<CancellationToken, ValueTask<IMavlinkPort>> _factory;

    public DelegatePortProvider(Func<CancellationToken, ValueTask<IMavlinkPort>> factory)
        => _factory = factory;

    public bool CanRecreatePort { get; init; } = true;

    public ValueTask<IMavlinkPort> CreatePortAsync(CancellationToken ct) => _factory(ct);
}