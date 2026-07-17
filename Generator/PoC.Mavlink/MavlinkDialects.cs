namespace Mavlink.Dialects;

public static class MavlinkDialects
{
    /// <summary>
    /// Merges several dialects into one. On message-id overlap the EARLIER
    /// dialect wins — overlap is normal in MAVLink (e.g. ardupilotmega
    /// includes common), so it is resolved by priority, not by throwing.
    /// </summary>
    public static IMavlinkDialect Combine(params IMavlinkDialect[] dialects)
    {
        if (dialects is null || dialects.Length == 0)
        {
            throw new ArgumentException("At least one dialect is required.", nameof(dialects));
        }

        return dialects.Length == 1
            ? dialects[0]
            : new MavlinkDialectCompositor(dialects);
    }

#if NET5_0_OR_GREATER
    /// <summary>
    /// A dialect that resolves messages against everything currently
    /// self-registered by generated dialects, in registration order
    /// (first registered wins on id overlap). Tracks the registry live,
    /// so late registrations are still picked up.
    /// </summary>
    public static IMavlinkDialect Auto { get; } = new AutoDialect();
 
    /// <summary>
    /// Diagnostic snapshot of all self-registered dialects, in registration
    /// order — e.g. for startup logging or a "Loaded dialects" UI.
    /// </summary>
    public static IReadOnlyList<IMavlinkDialect> Registered
        => Array.AsReadOnly(Infrastructure.MavlinkDialectRegistry.AllDialects);
 
    /// <summary>
    /// Live view over the dialect registry.
    ///
    /// Why the snapshot dance: MavlinkDialectCompositor caches lookup results
    /// INCLUDING negative ones (id → null), so a compositor built over an old
    /// registry state would keep answering null for messages of dialects
    /// registered later. The registry is copy-on-write (a new array instance
    /// on every Register), therefore "registry changed" == "array reference
    /// changed", and a single ReferenceEquals per lookup detects it without
    /// locking the hot path. On change we build a FRESH compositor (fresh,
    /// unpoisoned caches).
    ///
    /// Benign race (threading only, nothing to do with packets/dialect
    /// availability): two threads may notice the change simultaneously and
    /// both build a compositor from the same new array — identical results,
    /// one instance goes to GC. If a registration lands between the read and
    /// the write, the very next lookup re-detects the mismatch and rebuilds.
    /// Worst case is a duplicated cheap allocation, never a wrong answer.
    /// </summary>
    private sealed class AutoDialect : IMavlinkDialect
    {
        private IMavlinkDialect[] _snapshot = Array.Empty<IMavlinkDialect>();
        private MavlinkDialectCompositor _inner = new(Array.Empty<IMavlinkDialect>());
 
        public string Name => "auto";
 
        private MavlinkDialectCompositor Current
        {
            get
            {
                var all = Infrastructure.MavlinkDialectRegistry.AllDialects;
 
                if (!ReferenceEquals(all, _snapshot))
                {
                    _inner = new MavlinkDialectCompositor(all);
                    _snapshot = all;
                }
 
                return _inner;
            }
        }
 
        public IMavlinkMessageInfo? GetInfo(uint messageId) => Current.GetInfo(messageId);
 
        public IMavlinkMessageInfo? GetInfo(Type type) => Current.GetInfo(type);
    }
#endif
}
