using PzLauncher.Core.Net;

namespace PzLauncher.Desktop.Services;

/// <summary>The proxy page's state: the manual fields are kept outside manual mode, so switching back restores them.</summary>
/// <param name="Host"><c>null</c>: none saved, or the saved one was invalid.</param>
/// <param name="Password">Decrypted; <c>null</c> when none is saved or it couldn't be decrypted.</param>
public sealed record SavedProxy(ProxyMode Mode, string? Host, int? Port, string? Username, string? Password)
{
    public static readonly SavedProxy Default = new(ProxyMode.System, null, null, null, null);

    /// <summary>Unvalidated: missing manual fields become values that fail validation.</summary>
    public ProxySettings ToSettings() =>
        Mode == ProxyMode.Manual
            ? new ProxySettings(Mode, new ManualProxy(Host ?? "", Port ?? 0, Username, Password))
            : new ProxySettings(Mode, null);
}
