using System.Net;

namespace PzLauncher.Core.Net;

/// <summary>Set once as the handler's proxy; the handler asks it per request, so <see cref="Use"/> takes effect for the next request.</summary>
/// <remarks>
/// Must never throw: the handler treats a throwing proxy as "no proxy". The handler reads
/// <see cref="Credentials"/> once, so it is one forwarder for the client's lifetime.
/// </remarks>
internal sealed class SwitchableProxy : IWebProxy
{
    private readonly IWebProxy _system;
    private readonly ICredentials _credentials;

    // null: direct connection.
    private volatile IWebProxy? _current;

    /// <param name="system"><see cref="HttpClient.DefaultProxy"/> in production; on Windows it follows Internet Options changes by itself.</param>
    public SwitchableProxy(IWebProxy system)
    {
        _system = system;
        _current = system;
        _credentials = new ForwardingCredentials(this);
    }

    public ICredentials? Credentials
    {
        get => _credentials;
        set { }
    }

    /// <summary><paramref name="settings"/> must have passed <see cref="ProxySettingsValidation.Validate"/>.</summary>
    public void Use(ProxySettings settings) =>
        _current = settings.Mode switch
        {
            ProxyMode.System => _system,
            ProxyMode.None => null,
            ProxyMode.Manual => settings.Manual is { } manual ? CreateManual(manual) : null,
        };

    public Uri? GetProxy(Uri destination) => _current?.GetProxy(destination);

    public bool IsBypassed(Uri host) => _current?.IsBypassed(host) ?? true;

    private static WebProxy CreateManual(ManualProxy manual) =>
        // UriBuilder brackets an IPv6 host.
        new(new UriBuilder(Uri.UriSchemeHttp, manual.Host, manual.Port).Uri, BypassOnLocal: true)
        {
            Credentials = manual.Username is { } username ? new NetworkCredential(username, manual.Password ?? "") : null,
        };

    private sealed class ForwardingCredentials(SwitchableProxy owner) : ICredentials
    {
        public NetworkCredential? GetCredential(Uri uri, string authType) => owner._current?.Credentials?.GetCredential(uri, authType);
    }
}
