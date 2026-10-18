namespace PzLauncher.Core.Common;

/// <summary>The common <see cref="Error.Details"/> key sets; key names are part of the wire contract.</summary>
internal static class ErrorDetails
{
    public static Dictionary<string, string> Path(string path) => new() { ["path"] = path };

    public static Dictionary<string, string> Path(string path, string reason) => new() { ["path"] = path, ["reason"] = reason };
}
