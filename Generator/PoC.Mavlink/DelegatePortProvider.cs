namespace Mavlink;

public sealed class DelegatePortProvider : IMavlinkPortProvider
{
    private readonly Func<CancellationToken, ValueTask<IMavlinkPort>> _factory;

#if DEBUG
    private IMavlinkPort? _lastPort;
#endif

    public DelegatePortProvider(Func<CancellationToken, ValueTask<IMavlinkPort>> factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public bool CanRecreatePort { get; init; } = true;

    public bool CanWrite { get; init; } = true;

    public async ValueTask<IMavlinkPort> CreatePortAsync(CancellationToken ct)
    {
        var port = await _factory(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Port factory returned null.");

#if DEBUG
        if (ReferenceEquals(port, _lastPort))
        {
            throw new InvalidOperationException(
                "Port factory returned the same IMavlinkPort instance twice. " +
                "The factory must construct a new port on every call; " +
                "the previous instance has already been disposed by the channel.");
        }
        _lastPort = port;
#endif

        return port;
    }
}