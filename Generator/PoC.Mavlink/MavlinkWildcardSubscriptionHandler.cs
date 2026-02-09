namespace Mavlink;

internal sealed class MavlinkWildcardSubscriptionHandler : IMavlinkSubscriptionHandler
{
    private readonly Action<IMavlinkMessage, MavlinkContext> _callback;
    private readonly Func<MavlinkContext, bool>? _filter;

    public MavlinkWildcardSubscriptionHandler(
        Action<IMavlinkMessage, MavlinkContext> callback,
        Func<MavlinkContext, bool>? filter)
    {
        _callback = callback;
        _filter = filter;
    }

    public void Invoke(in MavlinkContext context)
    {
        if (_filter == null || _filter(context))
        {
            _callback(context.Message, context);
        }
    }
}