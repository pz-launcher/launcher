namespace PzLauncher.Core.Net;

public enum ProxyMode
{
    /// <summary>The OS proxy: Internet Options on Windows, system settings on macOS, <c>http_proxy</c>-style variables on Linux.</summary>
    System,
    None,
    Manual,
}

/// <param name="Host">Host name or IP address without a scheme.</param>
/// <param name="Username"><c>null</c> sends no credentials.</param>
public sealed record ManualProxy(string Host, int Port, string? Username, string? Password);

/// <param name="Manual">Non-null exactly when <paramref name="Mode"/> is <see cref="ProxyMode.Manual"/>.</param>
public sealed record ProxySettings(ProxyMode Mode, ManualProxy? Manual)
{
    public static readonly ProxySettings Default = new(ProxyMode.System, null);
}
