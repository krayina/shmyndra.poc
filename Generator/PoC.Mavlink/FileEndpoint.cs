namespace Mavlink.Transport;

/// <summary>
/// Read-only replay of a raw MAVLink byte log. String forms:
///   file:C:/logs/flight.tlog
///   file:///var/log/mav/flight.bin
///
/// EOF surfaces as read-returns-0 → the channel transitions to ConnectionLost.
/// SupportsReconnect is false, so the factories pick NoReconnectPolicy, and
/// the provider declares CanRecreatePort/CanWrite = false — sends throw a
/// clear "read-only transport" error instead of writing into a log file.
/// </summary>
public sealed class FileEndpoint : MavlinkEndpoint
{
    public FileEndpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        Path = path;
    }

    public string Path { get; }

    public override bool SupportsReconnect => false;

    internal static bool TryParseScheme(
        MavlinkConnectionStringParts parts,
        out MavlinkEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        var body = Uri.UnescapeDataString(parts.Body);

        // "file:///C:/logs/x.tlog" tokenizes to "/C:/logs/x.tlog" — strip the
        // leading slash for Windows drive paths.
        if (body.Length >= 3 && body[0] == '/' && char.IsLetter(body[1]) && body[2] == ':')
        {
            body = body.Substring(1);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "File path is empty. Expected e.g. 'file:C:/logs/flight.tlog'.";
            return false;
        }

        endpoint = new FileEndpoint(body);
        return true;
    }

    public override IMavlinkPortProvider CreateProvider()
    {
        var path = Path;

        return new DelegatePortProvider(ct =>
        {
            var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            return new ValueTask<IMavlinkPort>(new MavlinkStreamPort(stream));
        })
        {
            CanRecreatePort = false,
            CanWrite = false,
        };
    }

    public override string ToString() => $"file:{Path}";
}
