using System.Net;
using System.Net.Sockets;

namespace Mavlink.Transport;

/// <summary>
/// UDP transport. String forms:
///   udp://192.168.1.10:14550, udpout://..., udpcl://... → Client
///   udp://:14550, udp://@:14550, udp://0.0.0.0:14550, udpin://:14550 → Listen
/// Query: ?local=14551 (bind local port in Client mode), ?broadcast=true
/// </summary>
public sealed class UdpEndpoint : MavlinkEndpoint
{
    public UdpEndpoint()
    {
    }

    /// <summary>
    /// Listen on the given local port (GCS-style).
    /// </summary>
    public UdpEndpoint(int listenPort)
    {
        Mode = UdpEndpointMode.Listen;
        Port = listenPort;
    }

    /// <summary>
    /// Connected UDP client sending to host:port.
    /// </summary>
    public UdpEndpoint(string host, int port)
    {
        Mode = UdpEndpointMode.Client;
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;
    }

    public UdpEndpointMode Mode { get; set; } = UdpEndpointMode.Listen;

    /// <summary>
    /// Client mode: remote host to send to (required).
    /// Listen mode: local address to bind (null = any).
    /// </summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 14550;

    /// <summary>
    /// Client mode only: bind an explicit local port instead of an ephemeral one.
    /// </summary>
    public int? LocalPort { get; set; }

    /// <summary>
    /// Allow sending to broadcast addresses in Client mode.
    /// </summary>
    public bool AllowBroadcast { get; set; }

    internal static bool TryParseScheme(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;

        if (!ConnectionStringHelpers.TrySplitHostPort(
                parts.Body, defaultPort: 14550, out var host, out var port, out error))
        {
            return false;
        }

        UdpEndpointMode mode;
        switch (parts.Scheme)
        {
            case "udpin":
            case "udpl":
                mode = UdpEndpointMode.Listen;
                break;

            case "udpout":
            case "udpc":
            case "udpcl":
                if (ConnectionStringHelpers.IsWildcardHost(host))
                {
                    error = $"'{parts.Scheme}://' requires an explicit remote host.";
                    return false;
                }
                mode = UdpEndpointMode.Client;
                break;

            default:
                mode = ConnectionStringHelpers.IsWildcardHost(host)
                    ? UdpEndpointMode.Listen
                    : UdpEndpointMode.Client;
                break;
        }

        var ep = new UdpEndpoint
        {
            Mode = mode,
            Host = ConnectionStringHelpers.NormalizeHost(host),
            Port = port,
        };

        if (ConnectionStringHelpers.TryGetInt(parts.Query, "local", out var local)
            || ConnectionStringHelpers.TryGetInt(parts.Query, "bind", out local))
        {
            ep.LocalPort = local;
        }

        if (ConnectionStringHelpers.TryGetBool(parts.Query, "broadcast", out var broadcast))
        {
            ep.AllowBroadcast = broadcast;
        }

        endpoint = ep;
        return true;
    }

    public override IMavlinkPortProvider CreateProvider()
    {
        Validate();

        // Capture into locals so later mutation of the endpoint doesn't affect
        // an already-created channel.
        var mode = Mode;
        var host = Host;
        var port = Port;
        var localPort = LocalPort;
        var broadcast = AllowBroadcast;

        return new DelegatePortProvider(ct =>
        {
            if (mode == UdpEndpointMode.Client)
            {
                var udp = localPort is { } lp ? new UdpClient(lp) : new UdpClient();

                try
                {
                    udp.EnableBroadcast = broadcast;
                    udp.Connect(host!, port);
                    return new ValueTask<IMavlinkPort>(new MavlinkUdpPort(udp));
                }
                catch
                {
                    udp.Dispose();
                    throw;
                }
            }
            else
            {
                var bindAddress = host is null ? IPAddress.Any : IPAddress.Parse(host);
                var udp = new UdpClient(new IPEndPoint(bindAddress, port));
                return new ValueTask<IMavlinkPort>(new MavlinkUdpListenPort(udp));
            }
        });
    }

    private void Validate()
    {
        if (Port < 1 || Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be 1..65535.");
        }

        if (Mode == UdpEndpointMode.Client && string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException(
                "UdpEndpoint in Client mode requires Host. Use Listen mode to bind a local port.");
        }
    }

    public override string ToString()
    {
        if (Mode == UdpEndpointMode.Client)
        {
            return $"udpout://{Host}:{Port}";
        }
        return $"udpin://{Host ?? string.Empty}:{Port}";
    }
}
