#if NET5_0_OR_GREATER
using System.ComponentModel;

namespace Mavlink.Dialects.Infrastructure;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class MavlinkDialectRegistry
{
    private static readonly HashSet<IMavlinkDialect> _dialectsSet = new();
    private static IMavlinkDialect[] _cachedArray = Array.Empty<IMavlinkDialect>();
    private static readonly object _lock = new object();

    public static void Register(IMavlinkDialect dialect)
    {
        if (dialect == null)
        {
            return;
        }

        lock (_lock)
        {
            if (_dialectsSet.Add(dialect))
            {
                var newArray = new IMavlinkDialect[_dialectsSet.Count];
                _dialectsSet.CopyTo(newArray);
                _cachedArray = newArray;
            }
        }
    }

    internal static IMavlinkDialect[] AllDialects => _cachedArray;
}
#endif
