using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.LaunchSettings;
using PzLauncher.Desktop.ViewModels.Update;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

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

    private static readonly string SampleSecondLibrary =
        OperatingSystem.IsWindows() ? @"D:\SteamLibrary" : Path.Combine(SampleHome, "SteamLibrary");

    private static readonly string SampleJavaRoot = OperatingSystem.IsWindows() ? @"C:\Java" : "/opt/java";

    // Declared first: the properties below are initialized in textual order and need it.
    private static readonly LocalizationService Localization = new(AppLanguage.English);

    private static readonly NoPreferenceStore Preferences = new();

    public static InstallsViewModel Installs { get; } = CreateInstalls();

    private static readonly SampleGameLaunch GameLaunch = new();

    private static readonly SampleInstallOverrides InstallOverrides = new();

    private static readonly SampleZombieBuddy ZombieBuddySample = new();

    private static readonly SampleZombieBuddyChannels ZombieBuddyChannels = new();

    private static readonly GameRunState RunState = new();

    private static readonly SampleModCollections ModCollectionsSample = new();

    private static readonly SampleModCollectionDefaults ModCollectionDefaults = new();

    private static readonly ModCollectionApplier Applier = new(ModCollectionsSample, new NoDialogs());

    private static readonly ModCollectionOrderWriter OrderWriter = new(ModCollectionsSample, RunState, new SamplePlatform(), TimeProvider.System);

    public static HomeViewModel Home { get; } = CreateHome();

    public static ModsViewModel Mods { get; } = CreateMods();

    public static InstalledModsViewModel InstalledMods => Mods.Mods;

    public static ModCollectionsViewModel ModCollections => Mods.Collections;

    public static JavaRuntimesViewModel Java { get; } = CreateJava();

    private static readonly UpdateCoordinator Updates = CreateUpdates();

    public static UpdateSettingsViewModel UpdateSettings { get; } = new(Updates, Localization);

    public static UpdateBannerViewModel UpdateBanner { get; } = new(Updates, RunState, new NoUrlLauncher(), Localization);

    public static SettingsViewModel Settings { get; } = new(new LightThemeService(), Preferences, new NoWindowPlacement(), Java, UpdateSettings);

    public static LaunchSettingsViewModel LaunchSettings { get; } = CreateLaunchSettings();

    public static ZombieBuddySetupViewModel ZombieBuddySetup { get; } = CreateZombieBuddySetup();

    public static MainViewModel Main { get; } =
        new(Home, Installs, Mods, Settings, LaunchSettings, new LanguageMenuViewModel(Localization, Preferences), UpdateBanner);

    private static UpdateCoordinator CreateUpdates()
    {
        var coordinator = new UpdateCoordinator(new SampleUpdates(), Preferences, new UserPreferences(null, null, null), RunState, new NoAppLifetime());

        // Completes synchronously: the sample has a staged package, so the banner and the Ready status show.
        _ = coordinator.StartAsync();
        return coordinator;
    }

    private static string SampleAppPath(string installDir) =>
        Path.Combine(SampleSteamRoot, "steamapps", "common", installDir);

    private static InstallsViewModel CreateInstalls()
    {
        var vm = new InstallsViewModel(new SampleInstallDetection(), new NoFolderPicker(), new SamplePlatform(), Localization);
        vm.ScanCommand.Execute(null);
        return vm;
    }

    private static HomeViewModel CreateHome()
    {
        var vm = new HomeViewModel(
            Installs, GameLaunch, InstallOverrides, new NoDialogs(), ZombieBuddySample, ZombieBuddyChannels, new SampleZabric(), ModCollectionsSample,
            ModCollectionDefaults, Applier, OrderWriter, RunState);
        vm.ActivateCommand.Execute(null);
        return vm;
    }

    private static ModsViewModel CreateMods()
    {
        var vm = new ModsViewModel(
            Installs, ModCollectionsSample, InstallOverrides, ModCollectionDefaults, Applier, new NoDialogs(), new NoClipboard(), RunState, OrderWriter, Localization);
        vm.ActivateCommand.Execute(null);
        return vm;
    }

    private static string SampleJavaHome(string name) => Path.Combine(SampleJavaRoot, name);

    private static JavaRuntimesViewModel CreateJava()
    {
        var saved = new UserPreferences(null, null, null) { ManualJavaHomes = [SampleJavaHome("jdk-17-removed")] };
        var vm = new JavaRuntimesViewModel(new SampleJavaDiscovery(), new NoFolderPicker(), Preferences, saved, new SamplePlatform(), Localization);
        vm.ScanCommand.Execute(null);
        return vm;
    }

    private static LaunchSettingsViewModel CreateLaunchSettings()
    {
        var vm = new LaunchSettingsViewModel(
            Installs, GameLaunch, InstallOverrides, Java, ZombieBuddySample, ZombieBuddyChannels, RunState, new NoDialogs(), new SamplePlatform(), Localization);
        if (Installs.SelectedInstall is { } install)
        {
            vm.Open(install);
        }

        vm.ActivateCommand.Execute(null);
        return vm;
    }

    private static ZombieBuddySetupViewModel CreateZombieBuddySetup()
    {
        var vm = new ZombieBuddySetupViewModel(ZombieBuddySample, ZombieBuddySetupMode.Initialize, SampleAppPath("ProjectZomboid"), ZombieBuddyChannel.Stable, offersLaunch: true);
        _ = vm.StartAsync();
        return vm;
    }

    private sealed class SampleGameLaunch : IGameLaunchService
    {
        public Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default) =>
            Task.FromResult<Result<GameLaunchDefaults>>(new GameLaunchDefaults(
                MaxMemoryMb: 3072,
                MinMemoryMb: null,
                VmArgs: ["-Djava.awt.headless=true", "-Xmx3072m", "-XX:+UseZGC"],
                BundledJava: new JavaRuntime(
                    Path.Combine(installRoot, "jre64"),
                    Path.Combine(installRoot, "jre64", "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java"),
                    new JavaVersion(25, "25.0.1"),
                    "Azul Systems, Inc.",
                    JavaRuntimeSource.Manual,
                    HasGraalCompiler: false,
                    HasInstrumentLibrary: true),
                SystemMemoryMb: 16 * 1024,
                Warnings: [],
                GarbageCollector: GarbageCollector.Shenandoah,
                GarbageCollectorSetByViewpoint: true));

        public Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default) =>
            Task.FromResult<Result<GameSession>>(new Error(ErrorCode.LaunchPlatformUnsupported, "sample", ErrorKind.Validation));

        public Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default) =>
            Task.FromResult<Result>(new Error(ErrorCode.LaunchGcIncompatibleViewpoint, "sample", ErrorKind.Validation));

        public Task<Result> WaitForExitAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
    }

    private sealed class SampleInstallOverrides : IInstallOverridesService
    {
        public GameLaunchOverrides Get(string installRoot) =>
            new(SampleJavaHome("zulu-21"), MaxMemoryMb: 8192, MinMemoryMb: null, ExtraJvmArgs: "", ExtraGameArgs: "-debug", GarbageCollector: null);

        public void Save(string installRoot, GameLaunchOverrides overrides)
        {
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task ShowErrorAsync(string message) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(ConfirmPrompt prompt) => Task.FromResult(false);

        public Task<TextPromptResult> PromptTextAsync(TextPrompt prompt) => Task.FromResult<TextPromptResult>(new TextPromptResult.Cancelled());

        public Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup) => Task.FromResult(false);
    }

    // Both channels subscribed and initialized with allow-all on, so every part of the section shows; setup stays on its third step.
    private sealed class SampleZombieBuddy : IZombieBuddyService
    {
        private static readonly ZombieBuddyStep[] Steps =
            [ZombieBuddyStep.FindItem, ZombieBuddyStep.CopyNativeLoader, ZombieBuddyStep.CopyAgentJar, ZombieBuddyStep.AddAgentArg];

        private static string ItemPath(string id) =>
            Path.Combine(SampleSecondLibrary, "steamapps", "workshop", "content", "108600", id);

        public Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default) =>
            Task.FromResult<Result<ZombieBuddyStatus>>(new ZombieBuddyStatus(
                IsSupported: true,
                Subscribed: [new ZombieBuddyItem(ZombieBuddyChannel.Stable, ItemPath("1000010")), new ZombieBuddyItem(ZombieBuddyChannel.Beta, ItemPath("1000011"))],
                IsInitialized: true,
                HasLeftovers: true,
                IsAllowAll: true,
                ConfigFolder: Path.Combine(SampleHome, ".zombie_buddy"),
                NeededAction: ZombieBuddyAction.None));

        public Task<Result> InitializeAsync(
            string installRoot, ZombieBuddyChannel channel, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new ZombieBuddyProgress(Steps, 2, IsComplete: false));
            return new TaskCompletionSource<Result>().Task;
        }

        public Task<Result> RemoveAsync(string installRoot, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default) =>
            new TaskCompletionSource<Result>().Task;

        public Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default) => Task.FromResult(Result.Ok());
    }

    private sealed class SampleZabric : IZabricService
    {
        public Task<Result<ZombieBuddyPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default) =>
            Task.FromResult<Result<ZombieBuddyPreload>>(ZombieBuddyPreload.Current);
    }

    private sealed class SampleZombieBuddyChannels : IZombieBuddyChannelService
    {
        public ZombieBuddyChannel Get(string installRoot) => ZombieBuddyChannel.Stable;

        public void Save(string installRoot, ZombieBuddyChannel channel)
        {
        }
    }

    // A few fictional mods: one collection in use, one missing a mod and holding an incompatible pair.
    private sealed class SampleModCollections : IModCollectionService
    {
        private static readonly InstalledMod[] Installed =
        [
            Mod("ExampleCore", "Example Core Library", "1000101", InstalledModSource.SteamWorkshop),
            Mod("ExampleMaps", "Example Map Pack", "1000102", InstalledModSource.SteamWorkshop, require: ["ExampleCore"]),
            Mod("ExampleCars", "Example Vehicles", "1000103", InstalledModSource.SteamWorkshop, incompatible: ["ExampleCarsLite"]),
            Mod("ExampleCarsLite", "Example Vehicles Lite", "1000104", InstalledModSource.StagedWorkshop),
            Mod("ExampleTweaks", "Example Tweaks", null, InstalledModSource.Local),
        ];

        public Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default) =>
            Task.FromResult<Result<ModCollectionState>>(new ModCollectionState(
                Path.Combine(SampleHome, "Zomboid"),
                [
                    new ModCollection("Survival", ["ExampleCore", "ExampleMaps", "ExampleTweaks"], IsActive: true, IsActiveInOrder: true),
                    new ModCollection("Survival, tweaks first", ["ExampleTweaks", "ExampleCore", "ExampleMaps"], IsActive: true, IsActiveInOrder: false),
                    new ModCollection("Vehicles", ["ExampleCars", "ExampleCarsLite", "ExampleRemoved"], IsActive: false, IsActiveInOrder: false),
                ],
                Installed,
                ["ExampleCore", "ExampleMaps", "ExampleTweaks"],
                ActiveMatchesCollection: true));

        public Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default) => Task.FromResult(Result.Ok());

        public Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default) => Task.FromResult(Result.Ok());

        public Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default) =>
            Task.FromResult<Result<ApplyModCollectionResult>>(new ApplyModCollectionResult([], [], []));

        private static InstalledMod Mod(
            string id, string name, string? workshopId, InstalledModSource source, string[]? require = null, string[]? incompatible = null) =>
            new(id, name, workshopId, source, require ?? [], [], [], incompatible ?? []);
    }

    private sealed class SampleModCollectionDefaults : IModCollectionDefaultsService
    {
        public string? Get(string installRoot) => "Survival";

        public void Save(string installRoot, string name)
        {
        }

        public void Clear(string installRoot)
        {
        }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;

        public Task<string?> GetTextAsync() => Task.FromResult<string?>(null);
    }

    private sealed class SampleJavaDiscovery : IJavaDiscoveryService
    {
        public Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult<Result<JavaScanResult>>(new JavaScanResult(
                [
                    Runtime("jdk-25", new JavaVersion(25, "25.0.1"), "Eclipse Adoptium", JavaRuntimeSource.Registry),
                    Runtime("zulu-21", new JavaVersion(21, "21.0.5"), "Azul Systems, Inc.", JavaRuntimeSource.KnownFolder),
                    Runtime("custom-jre", null, null, JavaRuntimeSource.Path),
                ],
                [new Error(ErrorCode.JavaReleaseUnreadable, "sample", ErrorKind.External)]));

        public Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<Result<JavaRuntime>>(new Error(ErrorCode.JavaPathNotFound, "sample", ErrorKind.NotFound));

        private static JavaRuntime Runtime(string name, JavaVersion? version, string? vendor, JavaRuntimeSource source) =>
            new(SampleJavaHome(name), Path.Combine(SampleJavaHome(name), "bin", OperatingSystem.IsWindows() ? "javaw.exe" : "java"), version, vendor, source, HasGraalCompiler: false, HasInstrumentLibrary: true);
    }

    private sealed class SampleInstallDetection : IInstallDetectionService
    {
        public Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult<Result<InstallScanResult>>(new InstallScanResult(
                [
                    new PzInstall(PzInstallKind.Game, SampleAppPath("ProjectZomboid"), new SteamAppInfo("1000001", null)),
                    new PzInstall(PzInstallKind.Game, Path.Combine(SampleSecondLibrary, "steamapps", "common", "ProjectZomboid"), new SteamAppInfo("1000003", "unstable")),
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
        public UserPreferences Load() => new(null, null, null);

        public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveUpdateSourceAsync(UpdateSource source, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveAutoCheckUpdatesAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SampleUpdates : IUpdateService
    {
        private static readonly SemVersion Next = new(0, 3, 0, "");

        private static readonly Uri ReleasePage = new("https://example.com/releases/v0.3.0");

        public SemVersion CurrentVersion { get; } = new(0, 2, 0, "");

        public bool IsDevelopmentBuild => false;

        public bool CanInstallInPlace => true;

        public Task<Result<UpdateCheck>> CheckAsync(UpdateSource source, CancellationToken ct = default) =>
            Task.FromResult<Result<UpdateCheck>>(new UpdateCheck(CurrentVersion, null));

        public Task<Result<StagedUpdate>> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult<Result<StagedUpdate>>(Staged);

        public Task<StagedUpdate?> GetStagedAsync(CancellationToken ct = default) => Task.FromResult<StagedUpdate?>(Staged);

        public Task<Result> InstallAsync(StagedUpdate staged, CancellationToken ct = default) => Task.FromResult(Result.Ok());

        public Task<Result> StartInstalledAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

        public Task CleanUpLeftoversAsync(CancellationToken ct = default) => Task.CompletedTask;

        private static StagedUpdate Staged => new(Next, Path.Combine(SampleHome, "PzLauncher", ".update", "staged", "0.3.0"), ReleasePage);
    }

    private sealed class NoUrlLauncher : IUrlLauncher
    {
        public Task<bool> OpenAsync(Uri url) => Task.FromResult(false);
    }

    private sealed class NoAppLifetime : IAppLifetime
    {
        public void Close()
        {
        }
    }

    private sealed class NoWindowPlacement : IWindowPlacementService
    {
        public void ResetToDefault()
        {
        }
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

        public Version OsVersion { get; } = new(10, 0, 26100, 0);

        public long TotalMemoryBytes => 16L * 1024 * 1024 * 1024;

        public StringComparison PathComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public string? GetEnvironmentVariable(string name) => null;

        public string? ReadSteamPathFromRegistry() => null;

        public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path) => [];

        public string? ReadRegistryString(RegistryHiveKind hive, string path, string name) => null;
    }
}
