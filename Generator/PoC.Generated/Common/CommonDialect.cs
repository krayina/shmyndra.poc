using Mavlink.Common;
using Mavlink.Common.Codecs.Metadata;

namespace Mavlink.Dialects;

public sealed class CommonDialect : IMavlinkDialect
{
#if NET5_0_OR_GREATER
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() => MavlinkDialectRegistry.Register(Instance);
#endif

    public static readonly CommonDialect Instance = new CommonDialect();

    private CommonDialect() { }

    public string Name => "common";

    // For AOT compilation. Also could works with dictionary cache.
    public IMavlinkMessageInfo? GetInfo(uint msgId) => msgId switch
    {
        0 => HeartbeatMessageInfo.Instance,
        _ => null
    };

    // For AOT compilation. Also could works with dictionary cache.
    public IMavlinkMessageInfo? GetInfo(Type type)
    {
        if (type == typeof(HeartbeatMavlinkMessage)) return HeartbeatMessageInfo.Instance;
        return null;
        // return _typeMap.TryGetValue(type, out var info) ? info : null;
    }
}