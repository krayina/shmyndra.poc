namespace Mavlink;

internal sealed class MavlinkSubscriptionList
{
    private volatile IMavlinkSubscriptionHandler[] _handlers = Array.Empty<IMavlinkSubscriptionHandler>();
    private readonly object _lock = new object();

    public IMavlinkSubscriptionHandler[] Snapshot => _handlers;

    public void Add(IMavlinkSubscriptionHandler handler)
    {
        lock (_lock)
        {
            var old = _handlers;
            var next = new IMavlinkSubscriptionHandler[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            _handlers = next;
        }
    }

    public void Remove(IMavlinkSubscriptionHandler handler)
    {
        lock (_lock)
        {
            var old = _handlers;
            int idx = Array.IndexOf(old, handler);
            if (idx < 0)
            {
                return;
            }

            if (old.Length == 1)
            {
                _handlers = Array.Empty<IMavlinkSubscriptionHandler>();
                return;
            }

            var next = new IMavlinkSubscriptionHandler[old.Length - 1];
            if (idx > 0)
            {
                Array.Copy(old, 0, next, 0, idx);
            }
            if (idx < old.Length - 1)
            {
                Array.Copy(old, idx + 1, next, idx, old.Length - idx - 1);
            }

            _handlers = next;
        }
    }
}