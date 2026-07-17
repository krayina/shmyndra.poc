namespace Mavlink.Dialects;

#if NET5_0_OR_GREATER
    /// <summary>
    /// Live view over MavlinkDialectRegistry: rebuilds its cached compositor
    /// whenever the registry snapshot changes (registration is rare, lookups are
    /// hot — the ReferenceEquals check is a single read on the hot path).
    /// </summary>
    internal sealed class RegistryDialect : IMavlinkDialect
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
                    // Benign race: worst case two threads build the same compositor.
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
