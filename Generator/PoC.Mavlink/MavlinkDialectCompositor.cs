using Mavlink.Dialects;
using System.Collections.Concurrent;

namespace Mavlink;

internal sealed class MavlinkDialectCompositor : IMavlinkDialect
{
    private readonly IMavlinkDialect[] _dialects;

    private readonly ConcurrentDictionary<uint, IMavlinkMessageInfo?> _idCache = new();
    private readonly ConcurrentDictionary<Type, IMavlinkMessageInfo?> _typeCache = new();

    public MavlinkDialectCompositor(IMavlinkDialect[] dialects)
    {
        _dialects = dialects ?? Array.Empty<IMavlinkDialect>();
    }

    public string Name => "DialectCompositor";

    public IMavlinkMessageInfo? GetInfo(Type type)
    {
        if (_typeCache.TryGetValue(type, out var info))
        {
            return info;
        }

        info = FindInDialects(type);
        _typeCache.TryAdd(type, info);

        return info;
    }

    public IMavlinkMessageInfo? GetInfo(uint messageId)
    {
        if (_idCache.TryGetValue(messageId, out var info))
        {
            return info;
        }

        info = FindInDialects(messageId);
        _idCache.TryAdd(messageId, info);

        return info;
    }

    private IMavlinkMessageInfo? FindInDialects(Type type)
    {
        for (int i = 0; i < _dialects.Length; i++)
        {
            var info = _dialects[i].GetInfo(type);
            if (info != null) return info;
        }
        return null;
    }

    private IMavlinkMessageInfo? FindInDialects(uint messageId)
    {
        for (int i = 0; i < _dialects.Length; i++)
        {
            var info = _dialects[i].GetInfo(messageId);
            if (info != null) return info;
        }
        return null;
    }
}
