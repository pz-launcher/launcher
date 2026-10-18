using PzLauncher.Core.Common;

namespace PzLauncher.Core.Net;

public static class ProxySettingsValidation
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    private const string HttpPrefix = "http://";

    /// <summary>Normalized: host without the optional <c>http://</c> prefix, empty credentials as <c>null</c>, no manual proxy outside manual mode.</summary>
    public static Result<ProxySettings> Validate(ProxySettings settings)
    {
        if (settings.Mode != ProxyMode.Manual)
        {
            return settings with { Manual = null };
        }

        if (settings.Manual is not { } manual)
        {
            return NetworkErrors.ProxyHostInvalid("");
        }

        if (NormalizeHost(manual.Host) is not { } host)
        {
            return NetworkErrors.ProxyHostInvalid(manual.Host);
        }

        if (!IsValidPort(manual.Port))
        {
            return NetworkErrors.ProxyPortInvalid(manual.Port);
        }

        var username = string.IsNullOrEmpty(manual.Username) ? null : manual.Username;
        var password = username is null || string.IsNullOrEmpty(manual.Password) ? null : manual.Password;
        return settings with { Manual = new ManualProxy(host, manual.Port, username, password) };
    }

    /// <summary><c>null</c> when <paramref name="host"/> is not a host name or IP address, optionally prefixed with <c>http://</c>.</summary>
    public static string? NormalizeHost(string host)
    {
        var trimmed = host.Trim();
        if (trimmed.StartsWith(HttpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[HttpPrefix.Length..];
        }

        // A pasted "http://host/" keeps its slash; anything else after the host (path, port, credentials) is rejected.
        if (trimmed.EndsWith('/'))
        {
            trimmed = trimmed[..^1];
        }

        return Uri.CheckHostName(trimmed) switch
        {
            UriHostNameType.Unknown => null,
            // Uri also takes legacy short forms ("7890" is 0.0.30.210, "1.2.3" is 1.2.0.3); only a dotted quad is meant as an IPv4 address.
            UriHostNameType.IPv4 when trimmed.AsSpan().Count('.') != 3 => null,
            _ => trimmed,
        };
    }

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;
}
