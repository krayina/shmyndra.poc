using Mavlink.Dialects;
using System.Collections.Concurrent;

namespace Mavlink;

internal sealed class MavlinkEventBus
{
    private readonly IMavlinkDialect _dialect;
    private readonly ConcurrentDictionary<uint, MavlinkReceivedPacketCallbackRegistry> _typed = new();
    private readonly MavlinkReceivedPacketCallbackRegistry _all = new();

    internal event Action<Exception>? ErrorReceived;

    public MavlinkEventBus(IMavlinkDialect dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
    }

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
        where T : struct, IMavlinkMessage
    {
        var info = _dialect.GetInfo(typeof(T)) as IMavlinkMessageInfo<T>
            ?? throw new ArgumentException($"Type {typeof(T).Name} not registered in dialect.");

        var handler = new MavlinkReceivedPacketCallback<T>(callback, filter, info);
        var list = _typed.GetOrAdd(info.MessageId, _ => new MavlinkReceivedPacketCallbackRegistry());
        list.Add(handler);
        return new MavlinkSubscription(() => list.Remove(handler));
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkReceivedPacket> callback,
        Func<MavlinkReceivedPacket, bool>? filter = null)
    {
        var handler = new MavlinkReceivedPacketCallback(callback, filter, _dialect);
        _all.Add(handler);
        return new MavlinkSubscription(() => _all.Remove(handler));
    }

    public void Publish(in MavlinkReceivedPacket context)
    {
        if (_typed.TryGetValue(context.MessageId, out var list))
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
