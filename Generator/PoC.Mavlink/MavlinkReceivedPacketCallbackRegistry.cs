namespace Mavlink;

internal sealed class MavlinkReceivedPacketCallbackRegistry
{
    private volatile IMavlinkReceivedPacketCallback[] _handlers = Array.Empty<IMavlinkReceivedPacketCallback>();
    private readonly object _lock = new object();

    public IMavlinkReceivedPacketCallback[] Snapshot => _handlers;

    public void Add(IMavlinkReceivedPacketCallback handler)
    {
        lock (_lock)
        {
            var old = _handlers;
            var next = new IMavlinkReceivedPacketCallback[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            _handlers = next;
        }
    }

    public void Remove(IMavlinkReceivedPacketCallback handler)
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
                _handlers = Array.Empty<IMavlinkReceivedPacketCallback>();
                return;
            }

            var next = new IMavlinkReceivedPacketCallback[old.Length - 1];
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
