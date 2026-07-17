using Mavlink.Dialects;
using Mavlink.Transport;

namespace Mavlink;

public sealed partial class MavlinkClient
{
    /// <summary>
    /// Creates a client that OWNS its channel: disposing the client also
    /// disposes the channel (which the caller never sees, so nobody else
    /// could dispose it). For a shared channel with multiple client
    /// identities, use <see cref="MavlinkChannel.CreateClient"/> instead —
    /// those clients do NOT dispose the shared channel.
    /// <c>dialect: null</c> → <see cref="MavlinkDialects.Auto"/> on net5.0+;
    /// on netstandard an explicit dialect is required.
    /// </summary>
    public static MavlinkClient Create(
        string connectionString,
        byte systemId = 255,
        byte componentId = 190,
        IMavlinkDialect? dialect = null,
        Action<MavlinkChannelOptions>? configure = null)
    {
        return Create(MavlinkEndpoint.Parse(connectionString), systemId, componentId, dialect, configure);
    }

    /// <inheritdoc cref="Create(string, byte, byte, IMavlinkDialect?, Action{MavlinkChannelOptions}?)"/>
    public static MavlinkClient Create(
        MavlinkEndpoint endpoint,
        byte systemId = 255,
        byte componentId = 190,
        IMavlinkDialect? dialect = null,
        Action<MavlinkChannelOptions>? configure = null)
    {
        var channel = MavlinkChannel.Create(
            endpoint, MavlinkChannel.ResolveDialect(dialect), configure);

        return new MavlinkClient(
            channel, systemId, componentId,
            defaultSendVersion: null, ownsChannel: true);
    }

    /// <inheritdoc cref="Create(string, byte, byte, IMavlinkDialect?, Action{MavlinkChannelOptions}?)"/>
    public static Task<MavlinkClient> ConnectAsync(
        string connectionString,
        byte systemId = 255,
        byte componentId = 190,
        IMavlinkDialect? dialect = null,
        CancellationToken ct = default)
    {
        return ConnectAsync(MavlinkEndpoint.Parse(connectionString), systemId, componentId, dialect, null, ct);
    }

    /// <inheritdoc cref="Create(string, byte, byte, IMavlinkDialect?, Action{MavlinkChannelOptions}?)"/>
    public static async Task<MavlinkClient> ConnectAsync(
        MavlinkEndpoint endpoint,
        byte systemId = 255,
        byte componentId = 190,
        IMavlinkDialect? dialect = null,
        Action<MavlinkChannelOptions>? configure = null,
        CancellationToken ct = default)
    {
        var client = Create(endpoint, systemId, componentId, dialect, configure);

        try
        {
            await client.ConnectAsync(ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
