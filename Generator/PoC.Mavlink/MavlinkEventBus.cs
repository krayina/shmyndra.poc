using System.Collections.Concurrent;

namespace Mavlink;

internal sealed class MavlinkEventBus
{
    private readonly ConcurrentDictionary<Type, MavlinkReceivedPacketCallbackRegistry> _typed = new();
    private readonly MavlinkReceivedPacketCallbackRegistry _all = new();

    internal event Action<Exception>? ErrorReceived;

    internal void RaiseError(Exception ex)
    {
        try
        {
            ErrorReceived?.Invoke(ex);
        }
        catch { /* prevent cascading failures */ }
    }

    public IDisposable Subscribe<T>(
        Action<T, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
        where T : IMavlinkMessage
    {
        var handler = new MavlinkReceivedPacketCallback<T>(callback, filter);
        var list = _typed.GetOrAdd(typeof(T), _ => new MavlinkReceivedPacketCallbackRegistry());
        list.Add(handler);
        return new MavlinkSubscription(() => list.Remove(handler));
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        var handler = new MavlinkReceivedPacketCallback(callback, filter);
        _all.Add(handler);
        return new MavlinkSubscription(() => _all.Remove(handler));
    }

    public void Publish(in MavlinkReceivedPacket context)
    {
        if (_typed.TryGetValue(context.Message.GetType(), out var list))
        {
            InvokeHandlers(list.Snapshot, in context);
        }

        InvokeHandlers(_all.Snapshot, in context);
    }

    private void InvokeHandlers(
        IMavlinkReceivedPacketCallback[] handlers,
        in MavlinkReceivedPacket context)
    {
        for (int i = 0; i < handlers.Length; i++)
        {
            try
            {
                handlers[i].Invoke(in context);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }
    }
}
