using System.Globalization;

namespace Mavlink.Transport;

internal static class ConnectionStringHelpers
{
    internal static readonly IReadOnlyDictionary<string, string> EmptyQuery
        = new Dictionary<string, string>(0);

    internal static bool LooksLikeComPort(string s)
    {
        if (s.Length < 4 || !s.StartsWith("com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (int i = 3; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsWildcardHost(string host)
    {
        return host.Length == 0
            || host == "@"
            || host == "*"
            || host == "+"
            || host == "0.0.0.0"
            || host == "::"
            || host == "[::]";
    }

    /// <summary>
    /// Wildcard markers become null; IPv6 brackets are stripped.
    /// </summary>
    internal static string? NormalizeHost(string host)
    {
        if (host.Length == 0 || host == "@" || host == "*" || host == "+")
        {
            return null;
        }

        if (host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']')
        {
            return host.Substring(1, host.Length - 2);
        }

        return host;
    }

    internal static bool TrySplitHostPort(
        string body, int defaultPort, out string host, out int port, out string? error)
    {
        host = string.Empty;
        port = defaultPort;
        error = null;

        if (body.Length == 0)
        {
            return true;
        }

        int colon;

        if (body[0] == '[')
        {
            // IPv6 literal: [::1]:14550
            int close = body.IndexOf(']');
            if (close < 0)
            {
                error = "Unterminated IPv6 literal.";
                return false;
            }

            host = body.Substring(0, close + 1);
            colon = body.IndexOf(':', close);
        }
        else
        {
            colon = body.LastIndexOf(':');
            host = colon >= 0 ? body.Substring(0, colon) : body;
        }

        if (colon >= 0 && colon < body.Length - 1)
        {
            var portPart = body.Substring(colon + 1);
            if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                || port < 1 || port > 65535)
            {
                error = $"Invalid port '{portPart}'.";
                return false;
            }
        }

        return true;
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = "true";
            }
            else
            {
                result[Uri.UnescapeDataString(pair.Substring(0, eq))]
                    = Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        }

        return result;
    }

    internal static bool TryGetInt(
        IReadOnlyDictionary<string, string> query, string key, out int value)
    {
        value = 0;
        return query.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryGetBool(
        IReadOnlyDictionary<string, string> query, string key, out bool value)
    {
        value = false;

        if (!query.TryGetValue(key, out var raw))
        {
            return false;
        }

        switch (raw.ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                value = true;
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
                value = false;
                return true;
            default:
                return false;
        }
    }
}
