using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.ViewModels.Design;

/// <summary>XAML previewer data only. Not used at runtime.</summary>
public static class DesignData
{
    // Fictional sample data only: never copy paths or build ids from a real machine into here.
    private static readonly string SampleHome =
        OperatingSystem.IsWindows() ? @"C:\Users\Player"
        : OperatingSystem.IsMacOS() ? "/Users/player"
        : "/home/player";

    private static readonly string SampleSteamRoot =
        OperatingSystem.IsWindows() ? @"C:\Games\Steam"
        : OperatingSystem.IsMacOS() ? Path.Combine(SampleHome, "Library", "Application Support", "Steam")
        : Path.Combine(SampleHome, ".local", "share", "Steam");

    // Declared first: the properties below are initialized in textual order and need it.
    private static readonly LocalizationService Localization = new(AppLanguage.English);

    private static readonly NoPreferenceStore Preferences = new();

    public static InstallsViewModel Installs { get; } = CreateInstalls();

    public static HomeViewModel Home { get; } = new(Installs);

    public static SettingsViewModel Settings { get; } = new(new LightThemeService(), Preferences);

    public static MainViewModel Main { get; } = new(Home, Installs, Settings, new LanguageMenuViewModel(Localization, Preferences));

    private static string SampleAppPath(string installDir) =>
        Path.Combine(SampleSteamRoot, "steamapps", "common", installDir);

    private static InstallsViewModel CreateInstalls()
    {
        var vm = new InstallsViewModel(new SampleInstallDetection(), new NoFolderPicker(), new SamplePlatform(), Localization);
        vm.ScanCommand.Execute(null);
        return vm;
    }

    private sealed class SampleInstallDetection : IInstallDetectionService
    {
        public Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult<Result<InstallScanResult>>(new InstallScanResult(
                [
                    new PzInstall(PzInstallKind.Game, SampleAppPath("ProjectZomboid"), new SteamAppInfo("1000001", null)),
                    new PzInstall(PzInstallKind.DedicatedServer, SampleAppPath("Project Zomboid Dedicated Server"), new SteamAppInfo("1000002", "unstable")),
                ],
                [new Error(ErrorCode.SteamLibraryUnreadable, "sample", ErrorKind.External)]));

        public Task<Result<PzInstall>> InspectAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<Result<PzInstall>>(new PzInstall(PzInstallKind.Game, path, Steam: null));
    }

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default) =>
            Task.FromResult<FolderPickResult>(new FolderPickResult.Cancelled());
    }

    private sealed class NoPreferenceStore : IPreferenceStore
    {
        public UserPreferences Load() => new(null, null);

        public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class LightThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Light;

        public void SetTheme(AppTheme theme)
        {
        }
    }

    private sealed class SamplePlatform : IPlatformEnvironment
    {
        public PlatformKind Os =>
            OperatingSystem.IsWindows() ? PlatformKind.Windows
            : OperatingSystem.IsMacOS() ? PlatformKind.MacOS
            : PlatformKind.Linux;

        public string HomeDirectory => SampleHome;

        public StringComparison PathComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public string? GetEnvironmentVariable(string name) => null;

        public string? ReadSteamPathFromRegistry() => null;
    }
}
