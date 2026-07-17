using System.Net;
using System.Net.Sockets;

namespace Mavlink.Transport;

/// <summary>
/// TCP transport. String forms:
///   tcp://192.168.1.10:5760          → Client
///   tcpin://:5760, tcp://0.0.0.0:5760 → Server (single client)
/// Query: ?nodelay=false, ?timeout=5000 (connect timeout, ms)
///
/// Server mode binds a fresh listener per (re)connect and accepts exactly one
/// client; when that client drops, the reconnect loop re-binds and waits for
/// the next one.
/// </summary>
public sealed class TcpEndpoint : MavlinkEndpoint
{
    public TcpEndpoint()
    {
    }

    public TcpEndpoint(string host, int port)
    {
        Mode = TcpEndpointMode.Client;
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;
    }

    public TcpEndpointMode Mode { get; set; } = TcpEndpointMode.Client;

    /// <summary>
    /// Client mode: remote host (required).
    /// Server mode: local address to bind (null = any).
    /// </summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 5760;

    public bool NoDelay { get; set; } = true;

    /// <summary>Client mode: per-attempt connect timeout. Null = OS default.</summary>
    public TimeSpan? ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal static bool TryParseScheme(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;

        if (!ConnectionStringHelpers.TrySplitHostPort(
                parts.Body, defaultPort: 5760, out var host, out var port, out error))
        {
            return false;
        }

        TcpEndpointMode mode;
        switch (parts.Scheme)
        {
            case "tcpin":
            case "tcps":
                mode = TcpEndpointMode.Server;
                break;

            case "tcpout":
            case "tcpc":
                mode = TcpEndpointMode.Client;
                break;

            default:
                mode = ConnectionStringHelpers.IsWildcardHost(host)
                    ? TcpEndpointMode.Server
                    : TcpEndpointMode.Client;
                break;
        }

        if (mode == TcpEndpointMode.Client && ConnectionStringHelpers.IsWildcardHost(host))
        {
            error = "TCP client requires an explicit remote host.";
            return false;
        }

        var ep = new TcpEndpoint
        {
            Mode = mode,
            Host = ConnectionStringHelpers.NormalizeHost(host),
            Port = port,
        };

        if (ConnectionStringHelpers.TryGetBool(parts.Query, "nodelay", out var noDelay))
        {
            ep.NoDelay = noDelay;
        }

        if (ConnectionStringHelpers.TryGetInt(parts.Query, "timeout", out var timeoutMs) && timeoutMs > 0)
        {
            ep.ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        endpoint = ep;
        return true;
    }

    public override IMavlinkPortProvider CreateProvider()
    {
        Validate();

        var mode = Mode;
        var host = Host;
        var port = Port;
        var noDelay = NoDelay;
        var connectTimeout = ConnectTimeout;

        return new DelegatePortProvider(async ct =>
        {
            TcpClient client;

            if (mode == TcpEndpointMode.Client)
            {
                client = await ConnectClientAsync(host!, port, connectTimeout, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                var bindAddress = host is null ? IPAddress.Any : IPAddress.Parse(host);
                var listener = new TcpListener(bindAddress, port);
                listener.Start(backlog: 1);

                try
                {
                    client = await AcceptAsync(listener, ct).ConfigureAwait(false);
                }
                finally
                {
                    // One connection per port instance; the listener is not
                    // needed once the client is accepted (or accept failed).
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                        // Suppress exception during listener stop
                    }
                }
            }

            client.NoDelay = noDelay;
            return WrapClient(client);
        });
    }

    private static IMavlinkPort WrapClient(TcpClient client)
    {
        return new MavlinkStreamPort(
            client.GetStream(),
            owner: client,
            // NetworkStream honors CancellationToken on modern runtimes but not
            // on .NET Framework (netstandard2.0 consumers) — the hook covers that.
            cancelHook: () => client.Close());
    }

    private static async Task<TcpClient> ConnectClientAsync(
        string host, int port, TimeSpan? timeout, CancellationToken ct)
    {
        var client = new TcpClient();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is { } t)
            {
                cts.CancelAfter(t);
            }

#if NET5_0_OR_GREATER
            try
            {
                await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new MavlinkConnectionException($"TCP connect to {host}:{port} timed out.");
            }
#else
            var connectTask = client.ConnectAsync(host, port);
 
            using (cts.Token.Register(static state =>
            {
                try
                {
                    ((TcpClient)state!).Close();
                }
                catch
                {
                    // Suppress exception during close
                }
            }, client))
            {
                try
                {
                    await connectTask.ConfigureAwait(false);
                }
                catch (Exception) when (cts.IsCancellationRequested)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
 
                    throw new MavlinkConnectionException($"TCP connect to {host}:{port} timed out.");
                }
            }
#endif
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<TcpClient> AcceptAsync(TcpListener listener, CancellationToken ct)
    {
#if NET6_0_OR_GREATER
        return await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
#else
        var acceptTask = listener.AcceptTcpClientAsync();
 
        using (ct.Register(static state =>
        {
            try
            {
                ((TcpListener)state!).Stop();
            }
            catch
            {
                // Suppress exception during listener stop
            }
        }, listener))
        {
            try
            {
                return await acceptTask.ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }
#endif
    }

    private void Validate()
    {
        if (Port < 1 || Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be 1..65535.");
        }

        if (Mode == TcpEndpointMode.Client && string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("TcpEndpoint in Client mode requires Host.");
        }
    }

    public override string ToString()
        => Mode == TcpEndpointMode.Client
            ? $"tcpout://{Host}:{Port}"
            : $"tcpin://{Host ?? string.Empty}:{Port}";
}
