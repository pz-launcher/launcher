using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Net;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.ViewModels.Net;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

/// <summary>Validates like Core; <see cref="Applied"/> stands for every route switch.</summary>
public sealed class FakeNetworkService : INetworkService
{
    public ProxySettings Proxy { get; private set; } = ProxySettings.Default;

    public List<ProxySettings> Applied { get; } = [];

    public Func<IReadOnlyList<ConnectionTestResult>> Results { get; set; } = () => [];

    /// <summary>Holds a test open until completed.</summary>
    public TaskCompletionSource? TestGate { get; set; }

    /// <summary>The settings in effect when each test started.</summary>
    public List<ProxySettings> Tests { get; } = [];

    public Result ApplyProxy(ProxySettings settings)
    {
        var validated = ProxySettingsValidation.Validate(settings);
        if (!validated.IsSuccess)
        {
            return validated.Error;
        }

        Proxy = validated.Value;
        Applied.Add(validated.Value);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<ConnectionTestResult>> TestConnectionAsync(CancellationToken ct = default)
    {
        Tests.Add(Proxy);
        if (TestGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return Results();
    }
}

public static class TestProxy
{
    public static ProxySettingsService Service(
        FakeNetworkService network, IPreferenceStore store, SavedProxy? saved = null, TimeProvider? time = null) =>
        new(network, store, new UserPreferences(null, null, null) { Proxy = saved }, time ?? new ManualTimeProvider(), NullLogger<ProxySettingsService>.Instance);

    public static ProxySettingsViewModel ViewModel(ILocalizationService localization, IPreferenceStore store)
    {
        var network = new FakeNetworkService();
        return new ProxySettingsViewModel(Service(network, store), network, new FakePlatformEnvironment(), localization);
    }
}
