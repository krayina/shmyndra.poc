namespace Mavlink;

internal sealed class MavlinkSubscriptionToken : IDisposable
{
    private Action? _onDispose;

    public MavlinkSubscriptionToken(Action onDispose)
    {
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
