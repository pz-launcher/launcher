using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Net;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Steam;
using PzLauncher.Desktop.Services.Update;
using PzLauncher.Desktop.Services.ZombieBuddy;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.LaunchSettings;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Mods.Modpacks;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.ViewModels.Net;
using PzLauncher.Desktop.ViewModels.Settings;
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

    private static readonly AfterLaunchSettings AfterLaunch =
        new(Preferences, new UserPreferences(null, null, null) { AfterLaunch = AfterLaunchAction.MinimizeToTray }, new SamplePlatform());

    private static readonly SampleModCollections ModCollectionsSample = new();

    private static readonly SampleModCollectionDefaults ModCollectionDefaults = new();

    private static readonly ModCollectionApplier Applier = new(ModCollectionsSample, new NoDialogs());

    private static readonly ModCollectionOrderWriter OrderWriter = new(ModCollectionsSample, RunState, new SamplePlatform(), TimeProvider.System);

    public static HomeViewModel Home { get; } = CreateHome();

    public static ModsViewModel Mods { get; } = CreateMods();

    public static InstalledModsViewModel InstalledMods => Mods.Mods;

    public static ModCollectionsViewModel ModCollections => Mods.Collections;

    public static WorkshopSubscriptionsViewModel WorkshopSubscriptions => Mods.WorkshopSubscriptions;

    public static WorkshopCollectionsViewModel WorkshopCollections => Mods.WorkshopCollections;

    public static WorkshopFavoritesViewModel WorkshopFavorites => Mods.WorkshopFavorites;

    public static LocalCollectionsViewModel LocalCollections => Mods.LocalCollections;

    public static ModpacksViewModel Modpacks => Mods.Modpacks;

    public static ModpackEditViewModel ModpackEdit { get; } = CreateModpackEdit();

    public static LocalCollectionPickViewModel LocalCollectionPick { get; } = new(CreateLocalCollections());

    public static WorkshopItemRow WorkshopRow => WorkshopSubscriptions.VisibleRows.First(row => row.IsDownloading);

    public static WorkshopCollectionRow WorkshopCollectionRow => WorkshopCollections.Remembered[0];

    public static WorkshopActionViewModel WorkshopAction { get; } = CreateWorkshopAction();

    public static WorkshopModPickViewModel WorkshopModPick { get; } = CreateWorkshopModPick();

    public static JavaRuntimesViewModel Java { get; } = CreateJava();

    private static readonly UpdateCoordinator Updates = CreateUpdates();

    public static UpdateSettingsViewModel UpdateSettings { get; } = new(Updates, Localization);

    public static UpdateBannerViewModel UpdateBanner { get; } = new(Updates, RunState, new NoUrlLauncher(), Localization);

    public static ProxySettingsViewModel ProxySettings { get; } = new(new SampleProxySettings(), new SampleNetwork(), new SamplePlatform(), Localization);

    public static SteamSettingsViewModel SteamSettings { get; } = CreateSteamSettings();

    public static SettingsViewModel Settings { get; } =
        new(new LightThemeService(), Preferences, new NoWindowPlacement(), AfterLaunch, Java, UpdateSettings, ProxySettings, SteamSettings);

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

    private static SteamSettingsViewModel CreateSteamSettings()
    {
        var launcher = OperatingSystem.IsWindows() ? @"C:\Games\PzLauncher\PzLauncher.exe" : Path.Combine(SampleHome, "PzLauncher", "PzLauncher");
        var vm = new SteamSettingsViewModel(
            new SteamLaunchHookFlow(new SampleSteamHooks(), new NoDialogs(), launcher),
            Preferences,
            new UserPreferences(null, null, null),
            new NoDialogs(),
            new SamplePlatform(),
            Localization);

        // Completes synchronously: the sample answers at once.
        _ = vm.LoadAsync();
        return vm;
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
        var workshop = new SampleWorkshop();
        var vm = new HomeViewModel(
            Installs, GameLaunch, InstallOverrides, new NoDialogs(), ZombieBuddySample, ZombieBuddyChannels, new SampleZombieBuddyMods(),
            ModCollectionsSample, ModCollectionDefaults, Applier, OrderWriter,
            new WorkshopLaunchCheck(workshop, TimeProvider.System, NullLogger<WorkshopLaunchCheck>.Instance),
            workshop, new SampleWorkshopActions(), RunState,
            new AfterLaunchFlow(AfterLaunch, new NoLauncherWindow(), new NoAppLifetime(), new NoDialogs(), GameLaunch));
        vm.ActivateCommand.Execute(null);
        return vm;
    }

    private static ModsViewModel CreateMods()
    {
        var workshop = new SampleWorkshop();
        var history = CreateWorkshopHistory();
        var userCollections = CreateWorkshopUserCollections();
        var favorites = CreateWorkshopFavorites();
        var localCollections = CreateLocalCollections();
        var vm = new ModsViewModel(
            Installs, ModCollectionsSample, InstallOverrides, ModCollectionDefaults, Applier, new NoDialogs(), new NoClipboard(), RunState, OrderWriter,
            workshop, workshop, new SampleWorkshopActions(), new NoUrlLauncher(), history, userCollections, favorites,
            new WorkshopTitles(history, userCollections, favorites, localCollections), localCollections, CreateModpacks(localCollections), Localization);
        vm.ActivateCommand.Execute(null);

        // Completes synchronously: the sample answers at once. One outdated item shows as downloading.
        _ = vm.WorkshopSubscriptions.ShowAsync();
        vm.WorkshopSubscriptions.VisibleRows.FirstOrDefault(row => row.NeedsUpdate)?.ShowDownload(36 * 1024 * 1024, 90 * 1024 * 1024);
        _ = vm.WorkshopCollections.LoadCollectionAsync(vm.WorkshopCollections.Remembered[0].Id);
        _ = vm.WorkshopCollections.ShowAsync();
        _ = vm.WorkshopFavorites.ShowAsync();
        _ = vm.LocalCollections.ShowAsync();
        _ = vm.Modpacks.ShowAsync();
        return vm;
    }

    private static WorkshopCollectionHistory CreateWorkshopHistory() =>
        new(Preferences, new UserPreferences(null, null, null)
        {
            WorkshopCollections = [new SavedWorkshopEntry(1000900, "Example Collection"), new SavedWorkshopEntry(1000901, null)],
        });

    private static WorkshopUserCollectionsCache CreateWorkshopUserCollections() => new(Preferences, new UserPreferences(null, null, null), TimeProvider.System);

    private static WorkshopFavorites CreateWorkshopFavorites() =>
        new(Preferences, new UserPreferences(null, null, null)
        {
            WorkshopFavorites = new SavedWorkshopFavorites(
                [new SavedWorkshopEntry(1000900, "Example Collection"), new SavedWorkshopEntry(1000902, null)],
                [new SavedWorkshopEntry(1000102, "Example Map Pack"), new SavedWorkshopEntry(1000107, "Example Weather Pack")]),
        });

    private static LocalCollections CreateLocalCollections() =>
        new(Preferences, new UserPreferences(null, null, null)
        {
            LocalCollections =
            [
                new SavedLocalCollection(
                    new Guid("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b"),
                    "Example Server",
                    [new SavedWorkshopEntry(1000103, "Example Vehicles"), new SavedWorkshopEntry(1000101, "Example Core Library"), new SavedWorkshopEntry(1000108, null)]),
                new SavedLocalCollection(new Guid("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d"), "Weather tests", [new SavedWorkshopEntry(1000105, "Example Weather")]),
            ],
        });

    // One modpack linked to a preset and a Steam collection, one independent of both its sources.
    private static ModpackStore CreateModpacks(LocalCollections localCollections) =>
        new(
            Preferences,
            new UserPreferences(null, null, null)
            {
                Modpacks = new Dictionary<string, IReadOnlyList<Modpack>>
                {
                    [SampleAppPath("ProjectZomboid")] =
                    [
                        new Modpack(
                            "Example Server",
                            new ModpackPreset("Survival", ["ExampleCore", "ExampleMaps", "ExampleTweaks"], IsLinked: true),
                            new ModpackCollection.Steam(1000900),
                            [1000105]),
                        new Modpack(
                            "Weather night",
                            new ModpackPreset("Old weather", ["ExampleWeather", "ExampleCore"], IsLinked: false),
                            new ModpackCollection.Local(null, "Weather tests", [new WorkshopEntry(1000105, "Example Weather")]),
                            [1000101]),
                    ],
                },
            },
            new SamplePlatform(),
            localCollections);

    private static ModpackEditViewModel CreateModpackEdit()
    {
        return new ModpackEditViewModel(
            UiText.Modpacks_EditTitle,
            new Modpack(
                "Weather night",
                new ModpackPreset("Old weather", ["ExampleWeather", "ExampleCore"], IsLinked: false),
                new ModpackCollection.Steam(1000901),
                []),
            _ => null,
            Mods.State?.Collections ?? [],
            [
                new ModpackSteamOption(1000900, "Example Collection", UiText.Mods_SectionWorkshopFavorites),
                new ModpackSteamOption(1000902, null, UiText.Mods_SectionWorkshopFavorites),
                new ModpackSteamOption(1000901, null, UiText.WorkshopCollections_Remembered),
            ],
            CreateLocalCollections().All);
    }

    private static WorkshopModPickViewModel CreateWorkshopModPick()
    {
        var cars = new InstalledMod("ExampleCars", "Example Vehicles", 1000103, InstalledModSource.SteamWorkshop, [], [], [], ["ExampleCarsLite"]);
        var carsLite = new InstalledMod("ExampleCarsLite", "Example Vehicles Lite", 1000103, InstalledModSource.SteamWorkshop, [], [], [], []);
        return new WorkshopModPickViewModel(
            [
                new WorkshopModPickItem(
                    1000103,
                    "Example Vehicles (1000103)",
                    [new WorkshopModChoice(cars, ["ExampleCarsLite"]), new WorkshopModChoice(carsLite, ["ExampleCars"]) { IsChecked = false }]),
            ],
            ["Example Weather Pack (1000107): Not subscribed", "1000108: Not subscribed"]);
    }

    private static WorkshopActionViewModel CreateWorkshopAction()
    {
        var vm = new WorkshopActionViewModel(
            new SampleWorkshopActions(),
            SampleAppPath("ProjectZomboid"),
            Core.Steam.Workshop.Actions.WorkshopAction.Update,
            [
                new WorkshopActionItemRow(1000102, "Example Map Pack"),
                new WorkshopActionItemRow(1000103, "Example Vehicles"),
                new WorkshopActionItemRow(1000105, "Example Weather"),
            ]);
        _ = vm.StartAsync();
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

        public Task<bool> WaitPastEarlyExitAsync(CancellationToken ct = default) => Task.FromResult(false);
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

        public Task<ConfirmOptionResult> ConfirmWithOptionAsync(ConfirmPrompt prompt, string optionText) =>
            Task.FromResult(new ConfirmOptionResult(false, false));

        public Task<ConfirmChoice> ChooseAsync(ConfirmPrompt prompt, string alternativeText) => Task.FromResult(ConfirmChoice.Cancel);

        public Task<TextPromptResult> PromptTextAsync(TextPrompt prompt) => Task.FromResult<TextPromptResult>(new TextPromptResult.Cancelled());

        public Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup) => Task.FromResult(false);

        public Task ShowWorkshopActionAsync(WorkshopActionViewModel action) => Task.CompletedTask;

        public Task<bool> PickWorkshopModsAsync(WorkshopModPickViewModel pick) => Task.FromResult(false);

        public Task<bool> PickLocalCollectionAsync(LocalCollectionPickViewModel pick) => Task.FromResult(false);

        public Task<bool> EditModpackAsync(ModpackEditViewModel edit) => Task.FromResult(false);

        public Task<T?> ChooseOneAsync<T>(ChoicePrompt<T> prompt)
            where T : struct => Task.FromResult<T?>(null);
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

        public Task<Result> EnsurePatchesJarAsync(
            string installRoot, string sourceJar, string jarFile, string packageName, CancellationToken ct = default) => Task.FromResult(Result.Ok());

        public Task<Result> RemovePatchesJarAsync(string installRoot, string jarFile, string packageName, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok());
    }

    private sealed class SampleZombieBuddyMods : IZombieBuddyModsService
    {
        public Task<IReadOnlyList<Error>> PrepareLaunchAsync(string installRoot, string extraGameArgs, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Error>>([]);
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
            Mod("ExampleCore", "Example Core Library", 1000101, InstalledModSource.SteamWorkshop),
            Mod("ExampleMaps", "Example Map Pack", 1000102, InstalledModSource.SteamWorkshop, require: ["ExampleCore"]),
            Mod("ExampleCars", "Example Vehicles", 1000103, InstalledModSource.SteamWorkshop, incompatible: ["ExampleCarsLite"]),
            Mod("ExampleCarsLite", "Example Vehicles Lite", null, InstalledModSource.StagedWorkshop),
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
            string id, string name, ulong? workshopId, InstalledModSource source, string[]? require = null, string[]? incompatible = null) =>
            new(id, name, workshopId, source, require ?? [], [], [], incompatible ?? []);
    }

    // Fictional items in every state; the mods match SampleModCollections.
    private sealed class SampleWorkshop : IWorkshopService, IWorkshopModsService
    {
        private static readonly DateTimeOffset Subscribed = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        public Task<Result<WorkshopSubscriptions>> GetSubscriptionsAsync(string installRoot, CancellationToken ct = default) =>
            Task.FromResult<Result<WorkshopSubscriptions>>(new WorkshopSubscriptions([.. Joined().Items.Select(item => item.Item)], Joined().Warnings));

        public Task<Result<WorkshopModSubscriptions>> GetSubscriptionsAsync(string installRoot, string extraGameArgs, CancellationToken ct = default) =>
            Task.FromResult<Result<WorkshopModSubscriptions>>(Joined());

        public Task<Result<IReadOnlyList<WorkshopItem>>> FindOutdatedForLaunchAsync(
            string installRoot, string extraGameArgs, string? collection, CancellationToken ct = default) =>
            Task.FromResult<Result<IReadOnlyList<WorkshopItem>>>(Array.Empty<WorkshopItem>());

        public Task<Result<IReadOnlyList<WorkshopIdKind>>> ClassifyAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default) =>
            Task.FromResult<Result<IReadOnlyList<WorkshopIdKind>>>(ids.Select(id => new WorkshopIdKind(id, false, null)).ToList());

        private static WorkshopModSubscriptions Joined() =>
            new(
                [
                    Item(1000101, "Example Core Library", WorkshopItemState.UpToDate, ["ExampleCore"], ["Survival", "Survival, tweaks first"]),
                    Item(1000102, "Example Map Pack", WorkshopItemState.NeedsUpdate, ["ExampleMaps"], ["Survival", "Survival, tweaks first"]),
                    Item(1000103, "Example Vehicles", WorkshopItemState.NeedsUpdate, ["ExampleCars", "ExampleCarsLite"], ["Vehicles"]),
                    Item(1000105, "Example Weather", WorkshopItemState.DisabledLocally, ["ExampleWeather"], []),
                    Item(1000106, null, WorkshopItemState.NotInstalled, [], []),
                ],
                [new Error(ErrorCode.WorkshopManifestUnreadable, "sample", ErrorKind.External)]);

        public Task<Result<IReadOnlyList<WorkshopItemDetails>>> CheckUpdatesAsync(IReadOnlyList<ulong> ids, CancellationToken ct = default) =>
            Task.FromResult<Result<IReadOnlyList<WorkshopItemDetails>>>(Array.Empty<WorkshopItemDetails>());

        // Subscribed items in several states, one not subscribed and one the Web API doesn't show.
        public Task<Result<WorkshopCollection>> GetCollectionAsync(ulong id, CancellationToken ct = default) =>
            Task.FromResult<Result<WorkshopCollection>>(new WorkshopCollection(
                id,
                "Example Collection",
                [
                    new WorkshopCollectionItem(1000101, null),
                    new WorkshopCollectionItem(1000102, null),
                    new WorkshopCollectionItem(1000103, null),
                    new WorkshopCollectionItem(1000107, new WorkshopItemDetails(1000107, "Example Weather Pack", "1000107", Subscribed, 12 * 1024 * 1024)),
                    new WorkshopCollectionItem(1000108, null),
                ]));

        public Task<Result<SteamAccount>> GetCurrentAccountAsync(CancellationToken ct = default) =>
            Task.FromResult<Result<SteamAccount>>(new SteamAccount(12345678, "Survivor"));

        private static WorkshopModItem Item(ulong id, string? title, WorkshopItemState state, string[] modIds, string[] collections)
        {
            var updated = new DateTimeOffset(2026, 9, 1, 18, 30, 0, TimeSpan.Zero).AddDays(id % 30);
            var folder = Path.Combine(SampleSecondLibrary, "steamapps", "workshop", "content", "108600", id.ToString(CultureInfo.InvariantCulture));
            var installed = state == WorkshopItemState.NotInstalled ? null : new WorkshopInstall(folder, "1000" + id, updated, (long)(id % 97) * 1024 * 1024);
            var latest = title is null ? null : new WorkshopItemDetails(id, title, state == WorkshopItemState.NeedsUpdate ? "2000" + id : "1000" + id, updated, 0);
            return new WorkshopModItem(
                new WorkshopItem(id, Subscribed, state == WorkshopItemState.DisabledLocally, installed, latest, state),
                [.. modIds.Select(modId => new InstalledMod(modId, modId, id, InstalledModSource.SteamWorkshop, [], [], [], []))],
                collections);
        }
    }

    // Steam accepts the action and downloads the first item; the run never finishes.
    private sealed class SampleWorkshopActions : IWorkshopActionRunner
    {
        public Task<Result<WorkshopActionOutcome>> RunAsync(
            string installRoot, WorkshopActionRequest request, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new WorkshopActionStarted());
            if (request.Ids is [var first, var second, ..])
            {
                progress?.Report(new WorkshopItemFinished(new WorkshopItemOutcome(first, null, 0)));
                progress?.Report(new WorkshopItemProgress(second, 36 * 1024 * 1024, 90 * 1024 * 1024));
            }

            return new TaskCompletionSource<Result<WorkshopActionOutcome>>().Task;
        }

        public Task<Result<WorkshopUserCollections>> ListCollectionsAsync(
            string installRoot, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default) =>
            Task.FromResult<Result<WorkshopUserCollections>>(new WorkshopUserCollections(
                12345678,
                [new WorkshopCollectionSummary(1000201, "Example Survival Pack", 42), new WorkshopCollectionSummary(1000202, null, 7)],
                [new WorkshopCollectionSummary(1000203, "Example Server Mods", 18)]));
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

        public Task SaveProxyAsync(SavedProxy proxy, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveSteamHookReminderAsync(bool remind, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveWorkshopCollectionsAsync(IReadOnlyList<SavedWorkshopEntry> collections, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveWorkshopUserCollectionsAsync(SavedWorkshopUserCollections collections, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveWorkshopFavoritesAsync(SavedWorkshopFavorites favorites, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveAfterLaunchAsync(AfterLaunchAction action, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveLocalCollectionsAsync(IReadOnlyList<SavedLocalCollection> collections, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveModpacksAsync(string installRoot, IReadOnlyList<Modpack> modpacks, CancellationToken ct = default) => Task.CompletedTask;
    }

    // Steam opens a launcher in another folder, so the account, the switch and the other path all show.
    private sealed class SampleSteamHooks : ISteamLaunchHookService
    {
        private static readonly SteamLaunchHookStatus Status = new(
            new SteamAccount(12345678, "Survivor"),
            SteamLaunchHookState.OtherLauncher,
            OperatingSystem.IsWindows() ? @"D:\Tools\PzLauncher\PzLauncher.exe" : "/opt/pzlauncher/PzLauncher",
            IsSteamRunning: false);

        public Task<Result<SteamLaunchHookStatus>> GetStatusAsync(string launcherExe, CancellationToken ct = default) =>
            Task.FromResult<Result<SteamLaunchHookStatus>>(Status);

        public Task<Result<SteamLaunchHookApplied>> SetAsync(string launcherExe, bool enabled, bool restartSteam, CancellationToken ct = default) =>
            Task.FromResult<Result<SteamLaunchHookApplied>>(new SteamLaunchHookApplied(Status, []));
    }

    private sealed class SampleProxySettings : IProxySettingsService
    {
        public SavedProxy Current { get; } = new(ProxyMode.Manual, "127.0.0.1", 7890, null, null);

        public Result Apply(SavedProxy proxy) => Result.Ok();

        public Result ApplyDebounced(SavedProxy proxy) => Result.Ok();

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SampleNetwork : INetworkService
    {
        public Core.Net.ProxySettings Proxy => Core.Net.ProxySettings.Default;

        public Result ApplyProxy(Core.Net.ProxySettings settings) => Result.Ok();

        public Task<IReadOnlyList<ConnectionTestResult>> TestConnectionAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectionTestResult>>(
            [
                new("GitHub", new Uri("https://example.com/github"), TimeSpan.FromMilliseconds(180), null),
                new("Gitee", new Uri("https://example.com/gitee"), TimeSpan.FromSeconds(15), new Error(ErrorCode.NetworkTimeout, "sample", ErrorKind.External)),
            ]);
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

    private sealed class NoLauncherWindow : ILauncherWindow
    {
        public bool IsHidden => false;

        public void Minimize()
        {
        }

        public void Hide()
        {
        }

        public void Restore()
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
