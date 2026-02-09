using System.Collections.Concurrent;

namespace Mavlink;

internal sealed class MavlinkDispatcher
{
    private readonly ConcurrentDictionary<Type, MavlinkSubscriptionList> _typed = new();
    private readonly MavlinkSubscriptionList _wildcard = new();

    public IDisposable Subscribe<T>(
        Action<T, MavlinkContext> callback,
        Func<MavlinkContext, bool>? filter = null)
        where T : IMavlinkMessage
    {
        var handler = new MavlinkTypedSubscriptionHandler<T>(callback, filter);
        var list = _typed.GetOrAdd(typeof(T), _ => new MavlinkSubscriptionList());
        list.Add(handler);
        return new MavlinkSubscriptionToken(() => list.Remove(handler));
    }

    public IDisposable SubscribeAll(
        Action<IMavlinkMessage, MavlinkContext> callback,
        Func<MavlinkContext, bool>? filter = null)
    {
        var handler = new MavlinkWildcardSubscriptionHandler(callback, filter);
        _wildcard.Add(handler);
        return new MavlinkSubscriptionToken(() => _wildcard.Remove(handler));
    }

    public void Dispatch(in MavlinkContext context)
    {
        if (_typed.TryGetValue(context.Message.GetType(), out var list))
        {
            var handlers = list.Snapshot;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i].Invoke(context);
                }
                catch
                {
                }
            }
        }

        var wildcards = _wildcard.Snapshot;
        for (int i = 0; i < wildcards.Length; i++)
        {
            try
            {
                wildcards[i].Invoke(context);
            }
            catch
            {
            }
        }
    }
}
