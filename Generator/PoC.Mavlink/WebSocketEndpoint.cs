using System.Net.WebSockets;

namespace Mavlink.Transport;

/// <summary>
/// WebSocket client transport. String forms:
///   ws://192.168.1.10:8080/mavlink
///   wss://gcs.example.com/stream?subprotocol=mavlink
/// The full URI is passed to the server; the 'subprotocol' query key is
/// additionally applied as Sec-WebSocket-Protocol.
/// </summary>
public sealed class WebSocketEndpoint : MavlinkEndpoint
{
    public WebSocketEndpoint(Uri uri)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));

        if (uri.Scheme != "ws" && uri.Scheme != "wss")
        {
            throw new ArgumentException("URI scheme must be ws:// or wss://.", nameof(uri));
        }
    }

    public WebSocketEndpoint(string uri) : this(new Uri(uri, UriKind.Absolute))
    {
    }

    public Uri Uri { get; }

    public string? SubProtocol { get; set; }

    public TimeSpan? KeepAliveInterval { get; set; }

    /// <summary>
    /// Extra hook for headers, proxy, certificates, etc.
    /// </summary>
    public Action<ClientWebSocketOptions>? ConfigureOptions { get; set; }

    internal static bool TryParseScheme(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        if (!Uri.TryCreate(parts.Original, UriKind.Absolute, out var uri))
        {
            error = "Invalid WebSocket URI.";
            return false;
        }

        var ep = new WebSocketEndpoint(uri);

        if (parts.Query.TryGetValue("subprotocol", out var sub)
            && !string.IsNullOrWhiteSpace(sub))
        {
            ep.SubProtocol = sub;
        }

        endpoint = ep;
        return true;
    }

    public override IMavlinkPortProvider CreateProvider()
    {
        var uri = Uri;
        var subProtocol = SubProtocol;
        var keepAlive = KeepAliveInterval;
        var configure = ConfigureOptions;

        return new DelegatePortProvider(async ct =>
        {
            var ws = new ClientWebSocket();

            try
            {
                if (!string.IsNullOrEmpty(subProtocol))
                {
                    ws.Options.AddSubProtocol(subProtocol);
                }

                if (keepAlive is { } ka)
                {
                    ws.Options.KeepAliveInterval = ka;
                }

                configure?.Invoke(ws.Options);

                await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                return new MavlinkWebSocketPort(ws);
            }
            catch (OperationCanceledException)
            {
                ws.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                ws.Dispose();
                throw new MavlinkConnectionException($"WebSocket connect to {uri} failed.", ex);
            }
        });
    }

    public override string ToString() => Uri.ToString();
}
