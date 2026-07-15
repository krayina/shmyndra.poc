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

    public IMavlinkMessageInfo? GetInfo(uint msgId) => msgId switch
    {
        0 => HeartbeatMessageInfo.Instance,
        76 => CommandLongMessageInfo.Instance,
        _ => null
    };

    public IMavlinkMessageInfo? GetInfo(Type type)
    {
        if (type == typeof(HeartbeatMavlinkMessage)) return HeartbeatMessageInfo.Instance;
        if (type == typeof(CommandLongMavlinkMessage)) return CommandLongMessageInfo.Instance;
        return null;
    }
}
