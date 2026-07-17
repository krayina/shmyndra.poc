namespace Mavlink.Transport;

/// <summary>
/// Raw pieces of a tokenized connection string, handed to scheme parsers.
/// </summary>
public sealed class MavlinkConnectionStringParts
{
    internal MavlinkConnectionStringParts(
        string original, string scheme, string body,
        IReadOnlyDictionary<string, string> query)
    {
        Original = original;
        Scheme = scheme;
        Body = body;
        Query = query;
    }

    /// <summary>
    /// The full original connection string.
    /// </summary>
    public string Original { get; }

    /// <summary>
    /// Lower-cased scheme, e.g. "udp".
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Everything between "scheme://" (or "scheme:") and '?'.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Parsed query parameters (case-insensitive keys).
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; }
}
