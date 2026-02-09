namespace Mavlink;

internal sealed class MavlinkSubscription : IDisposable
{
    private Action? _onDispose;

    public MavlinkSubscription(Action onDispose)
    {
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
