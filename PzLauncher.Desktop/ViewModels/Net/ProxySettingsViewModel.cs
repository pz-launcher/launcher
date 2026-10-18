using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Net;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.ViewModels.Net;

/// <summary>The Settings "Proxy" section.</summary>
public sealed partial class ProxySettingsViewModel : ViewModelBase
{
    private readonly IProxySettingsService _proxy;
    private readonly INetworkService _network;
    private readonly PlatformKind _os;

    // Set after the saved choice is shown, so showing it doesn't apply it again.
    private readonly bool _loaded;

    private Error? _error;
    private IReadOnlyList<ConnectionTestResult> _results = [];

    public ProxySettingsViewModel(IProxySettingsService proxy, INetworkService network, IPlatformEnvironment platform, ILocalizationService localization)
    {
        _proxy = proxy;
        _network = network;
        _os = platform.Os;

        var current = proxy.Current;
        Mode = current.Mode;
        Host = current.Host ?? "";
        Port = current.Port?.ToString(CultureInfo.InvariantCulture) ?? "";
        Username = current.Username ?? "";
        Password = current.Password ?? "";
        RenderTexts();
        _loaded = true;

        // Never unsubscribed: the localization service and this view model live as long as the app.
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    public partial ProxyMode Mode { get; set; }

    [ObservableProperty] public partial string Host { get; set; } = "";

    /// <summary>Text, so a half-typed or non-numeric port can show an error instead of being rejected by the binding.</summary>
    [ObservableProperty] public partial string Port { get; set; } = "";

    [ObservableProperty] public partial string Username { get; set; } = "";

    [ObservableProperty] public partial string Password { get; set; } = "";

    public bool IsManual => Mode == ProxyMode.Manual;

    /// <summary>DPAPI protects it on Windows only.</summary>
    public bool IsPasswordStoredInPlainText => _os != PlatformKind.Windows;

    [ObservableProperty] public partial string SystemHint { get; private set; } = "";

    /// <summary><c>null</c> while the fields are valid.</summary>
    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<ConnectionTestRow> TestResults { get; private set; } = [];

    partial void OnModeChanged(ProxyMode value)
    {
        if (_loaded)
        {
            Show(_proxy.Apply(Snapshot()));
        }
    }

    partial void OnHostChanged(string value) => OnFieldChanged();

    partial void OnPortChanged(string value) => OnFieldChanged();

    partial void OnUsernameChanged(string value) => OnFieldChanged();

    partial void OnPasswordChanged(string value) => OnFieldChanged();

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        _results = [];
        RenderTexts();

        // The test checks what the page shows, including an edit still waiting for its debounce.
        await _proxy.FlushAsync();
        _results = await _network.TestConnectionAsync();
        RenderTexts();
    }

    private void OnFieldChanged()
    {
        if (_loaded)
        {
            Show(_proxy.ApplyDebounced(Snapshot()));
        }
    }

    private SavedProxy Snapshot() =>
        new(
            Mode,
            Host.Trim() is { Length: > 0 } host ? host : null,
            int.TryParse(Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : null,
            Username.Length > 0 ? Username : null,
            Password.Length > 0 ? Password : null);

    private void Show(Result result)
    {
        _error = result.Error;
        RenderTexts();
    }

    private void RenderTexts()
    {
        SystemHint = _os switch
        {
            PlatformKind.Windows => UiText.Proxy_SystemHintWindows,
            PlatformKind.MacOS => UiText.Proxy_SystemHintMacOS,
            PlatformKind.Linux => UiText.Proxy_SystemHintLinux,
        };
        ErrorText = _error is { } error ? ErrorMessages.ToUserText(error) : null;
        TestResults = [.. _results.Select(ToRow)];
    }

    private static ConnectionTestRow ToRow(ConnectionTestResult result) =>
        result.Error is { } error
            ? new ConnectionTestRow(result.Name, ErrorMessages.ToUserText(error), IsError: true)
            : new ConnectionTestRow(
                result.Name,
                string.Format(CultureInfo.CurrentCulture, UiText.Proxy_TestOk, (int)Math.Round(result.Elapsed.TotalMilliseconds)),
                IsError: false);
}
