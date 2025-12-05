namespace Mavlink;

internal static class MavlinkInfoCache<T> where T : IMavlinkMessage
{
    public static readonly IMavlinkMessageInfo<T>? Info;

    static MavlinkInfoCache()
    {
        var untypedInfo = MavlinkDialectRegistry.GetInfo(typeof(T));
        if (untypedInfo != null)
        {
            Info = (IMavlinkMessageInfo<T>)untypedInfo;
        }
    }
}