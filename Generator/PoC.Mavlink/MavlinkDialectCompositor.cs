using Mavlink.Dialects;

namespace Mavlink;

internal sealed class MavlinkDialectCompositor : IMavlinkDialect
{
    private readonly IMavlinkDialect[] _dialects;

    public MavlinkDialectCompositor(IMavlinkDialect[] dialects)
    {
        _dialects = dialects;
    }

    public string Name => "DialectCompositor";

    public IMavlinkMessageInfo? GetInfo(Type type)
    {
        for (int i = 0; i < _dialects.Length; i++)
        {
            var info = _dialects[i].GetInfo(type);
            if (info != null)
            {
                return info;
            }
        }
        return null;
    }

    public IMavlinkMessageInfo? GetInfo(uint messageId)
    {
        for (int i = 0; i < _dialects.Length; i++)
        {
            var info = _dialects[i].GetInfo(messageId);
            if (info != null)
            {
                return info;
            }
        }
        return null;
    }
}
