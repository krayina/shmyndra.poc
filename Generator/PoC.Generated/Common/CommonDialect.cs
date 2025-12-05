using Mavlink.Common;
using Mavlink.Common.Codecs.Metadata;

namespace Mavlink.Dialects;

internal static class CommonDialect
{
    private static int _initialized = 0;
    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            MavlinkDialectRegistry.RegisterMessage(HeartbeatMessageInfo.Instance);
            MavlinkDialectRegistry.RegisterRouter(new CommonDialectRouter());
        }
    }

#if NET5_0_OR_GREATER
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() => EnsureInitialized();
#endif
}