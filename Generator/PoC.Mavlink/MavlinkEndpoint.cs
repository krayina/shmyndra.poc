using System.Collections.Concurrent;
using System.Globalization;

namespace Mavlink.Transport;

/// <summary>
/// A strongly-typed, transport-specific description of where and how to connect.
///
/// Tier 1 (simplest):  MavlinkChannel.Create("udp://:14550")
/// Tier 2 (typed):     MavlinkChannel.Create(new UdpEndpoint(14550))
/// Tier 3 (full):      MavlinkChannel.Create(new MavlinkChannelOptions { PortProvider = ... })
///
/// This base type only tokenizes the string (scheme / body / query) and routes
/// it to a scheme parser. All transport-specific knowledge lives in the
/// endpoint types themselves (UdpEndpoint.TryParseScheme etc.), and custom
/// user schemes registered via <see cref="RegisterScheme"/> go through the
/// same dictionary-based path (custom entries take precedence).
/// </summary>
public abstract class MavlinkEndpoint
{
    /// <summary>Creates a port provider for this endpoint. Called once per channel.</summary>
    public abstract IMavlinkPortProvider CreateProvider();

    /// <summary>
    /// False for one-shot transports (e.g. a finished file replay) where
    /// reconnecting makes no sense. Factories use this to pick the default
    /// reconnect policy (infinite backoff vs. NoReconnectPolicy).
    /// </summary>
    public virtual bool SupportsReconnect => true;

    internal delegate bool SchemeParser(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error);

    private static readonly Dictionary<string, SchemeParser> _builtInSchemes
        = new Dictionary<string, SchemeParser>(StringComparer.OrdinalIgnoreCase)
        {
            ["udp"] = UdpEndpoint.TryParseScheme,
            ["udpin"] = UdpEndpoint.TryParseScheme,
            ["udpl"] = UdpEndpoint.TryParseScheme,
            ["udpout"] = UdpEndpoint.TryParseScheme,
            ["udpc"] = UdpEndpoint.TryParseScheme,
            ["udpcl"] = UdpEndpoint.TryParseScheme,

            ["tcp"] = TcpEndpoint.TryParseScheme,
            ["tcpin"] = TcpEndpoint.TryParseScheme,
            ["tcps"] = TcpEndpoint.TryParseScheme,
            ["tcpout"] = TcpEndpoint.TryParseScheme,
            ["tcpc"] = TcpEndpoint.TryParseScheme,

            ["serial"] = SerialEndpoint.TryParseScheme,
            ["com"] = SerialEndpoint.TryParseScheme,

            ["ws"] = WebSocketEndpoint.TryParseScheme,
            ["wss"] = WebSocketEndpoint.TryParseScheme,

            ["file"] = FileEndpoint.TryParseScheme,
        };

    private static readonly ConcurrentDictionary<string, Func<MavlinkConnectionStringParts, MavlinkEndpoint>> _customSchemes
        = new ConcurrentDictionary<string, Func<MavlinkConnectionStringParts, MavlinkEndpoint>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a factory for a custom scheme, e.g. "mock://" or "replay://".
    /// Custom schemes take precedence over built-in ones.
    /// </summary>
    public static void RegisterScheme(
        string scheme, Func<MavlinkConnectionStringParts, MavlinkEndpoint> factory)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            throw new ArgumentException("Scheme must not be empty.", nameof(scheme));
        }

        _customSchemes[scheme.Trim()] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public static MavlinkEndpoint Parse(string connectionString)
    {
        if (!TryParse(connectionString, out var endpoint, out var error))
        {
            throw new FormatException(
                $"Invalid MAVLink connection string '{connectionString}': {error}");
        }

        return endpoint!;
    }

    public static bool TryParse(string? connectionString, out MavlinkEndpoint? endpoint)
    {
        return TryParse(connectionString, out endpoint, out _);
    }

    public static bool TryParse(
        string? connectionString,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "Connection string is empty.";
            return false;
        }

        var s = connectionString!.Trim();

        // Bare serial shortcuts: "COM3", "COM3:57600", "/dev/ttyUSB0:115200".
        if (s[0] == '/' || ConnectionStringHelpers.LooksLikeComPort(s))
        {
            return SerialEndpoint.TryParseScheme(
                new MavlinkConnectionStringParts(s, "serial", s, ConnectionStringHelpers.EmptyQuery),
                out endpoint, out error);
        }

        int schemeEnd = s.IndexOf(':');
        if (schemeEnd <= 0)
        {
            error = "Missing scheme. Expected e.g. 'udp://:14550', 'tcp://host:5760', 'serial:COM3?baud=57600'.";
            return false;
        }

        string rawScheme = s.Substring(0, schemeEnd);
        string scheme = rawScheme.ToLowerInvariant();
        string body = s.Substring(schemeEnd + 1);

        if (body.StartsWith("//", StringComparison.Ordinal))
        {
            body = body.Substring(2);
        }

        string queryString = string.Empty;
        int q = body.IndexOf('?');
        if (q >= 0)
        {
            queryString = body.Substring(q + 1);
            body = body.Substring(0, q);
        }

        var parts = new MavlinkConnectionStringParts(
            s, scheme, body, ConnectionStringHelpers.ParseQuery(queryString));

        // Custom schemes win over built-ins.
        if (_customSchemes.TryGetValue(scheme, out var custom))
        {
            try
            {
                endpoint = custom(parts);
                if (endpoint is null)
                {
                    error = $"Custom scheme factory for '{scheme}' returned null.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"Custom scheme factory for '{scheme}' failed: {ex.Message}";
                return false;
            }
        }

        if (_builtInSchemes.TryGetValue(scheme, out var parser))
        {
            return parser(parts, out endpoint, out error);
        }

        // "COM3:57600" tokenizes as scheme "com3" — rebuild the body
        // (query already stripped) and hand it to the serial parser.
        if (ConnectionStringHelpers.LooksLikeComPort(scheme))
        {
            var serialBody = body.Length == 0 ? rawScheme : rawScheme + ":" + body;
            return SerialEndpoint.TryParseScheme(
                new MavlinkConnectionStringParts(s, "serial", serialBody, parts.Query),
                out endpoint, out error);
        }

        error = $"Unknown scheme '{scheme}'. Built-in: udp, udpin, udpout (udpcl), tcp, tcpin, tcpout, serial, ws, wss, file.";
        return false;
    }
}
