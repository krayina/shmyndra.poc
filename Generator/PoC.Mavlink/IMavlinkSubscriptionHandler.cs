namespace Mavlink;

internal interface IMavlinkSubscriptionHandler
{
    void Invoke(in MavlinkContext context);
}