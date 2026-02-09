namespace Mavlink;

internal sealed class MavlinkTypedSubscriptionHandler<T> : IMavlinkSubscriptionHandler
    where T : IMavlinkMessage
{
    private readonly Action<T, MavlinkContext> _callback;
    private readonly Func<MavlinkContext, bool>? _filter;

    public MavlinkTypedSubscriptionHandler(
        Action<T, MavlinkContext> callback,
        Func<MavlinkContext, bool>? filter)
    {
        _callback = callback;
        _filter = filter;
    }

    public void Invoke(in MavlinkContext context)
    {
        if (context.Message is T typed
            && (_filter == null || _filter(context)))
        {
            _callback(typed, context);
        }
    }
}
