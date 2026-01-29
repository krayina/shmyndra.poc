#if NET5_0_OR_GREATER
using System.ComponentModel;

namespace Mavlink.Dialects;

public static class MavlinkDialectRegistry
{
    private static readonly HashSet<IMavlinkDialect> _dialectsSet = new();
    private static IMavlinkDialect[] _cachedArray = Array.Empty<IMavlinkDialect>();
    private static readonly object _lock = new object();

#if NETSTANDARD2_1_OR_GREATER
    [EditorBrowsable(EditorBrowsableState.Never)]
#endif
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

    public static IMavlinkDialect[] AllDialects => _cachedArray;
}
#endif
