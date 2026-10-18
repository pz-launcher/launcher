using System.Globalization;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Net;

internal static class NetworkErrors
{
    public static Error ProxyHostInvalid(string host) =>
        new(ErrorCode.NetworkProxyHostInvalid, $"Proxy host '{host}' is not a host name or IP address", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["host"] = host },
        };

    public static Error ProxyPortInvalid(int port) =>
        new(ErrorCode.NetworkProxyPortInvalid, $"Proxy port {port} is outside 1-65535", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["port"] = port.ToString(CultureInfo.InvariantCulture) },
        };

    public static Error Unreachable(string target, string reason) =>
        new(ErrorCode.NetworkUnreachable, $"{target} could not be reached ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["target"] = target, ["reason"] = reason },
        };

    public static Error Timeout(string target, TimeSpan timeout) =>
        new(ErrorCode.NetworkTimeout, $"{target} did not answer within {timeout.TotalSeconds:0} s", ErrorKind.External)
        {
            Details = new Dictionary<string, string>
            {
                ["target"] = target,
                ["seconds"] = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            },
        };
}
