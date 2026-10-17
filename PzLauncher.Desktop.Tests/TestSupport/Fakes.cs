using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Set <see cref="ScanGate"/> to hold a scan open and observe the busy state.</summary>
public sealed class FakeInstallDetectionService : IInstallDetectionService
{
    public Queue<Result<InstallScanResult>> ScanResults { get; } = new();

    public TaskCompletionSource? ScanGate { get; set; }

    public Func<string, Result<PzInstall>> Inspect { get; set; } =
        path => new PzInstall(PzInstallKind.Game, path, Steam: null);

    public int ScanCalls { get; private set; }

    public List<string> InspectedPaths { get; } = [];

    public async Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ScanCalls++;
        if (ScanGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return ScanResults.Count > 0 ? ScanResults.Dequeue() : new InstallScanResult([], []);
    }

    public Task<Result<PzInstall>> InspectAsync(string path, CancellationToken ct = default)
    {
        InspectedPaths.Add(path);
        return Task.FromResult(Inspect(path));
    }

    public static InstallScanResult Found(params PzInstall[] installs) => new(installs, []);
}

/// <summary>Set <see cref="ScanGate"/> to hold a scan open and observe the busy state.</summary>
public sealed class FakeJavaDiscoveryService : IJavaDiscoveryService
{
    public Queue<Result<JavaScanResult>> ScanResults { get; } = new();

    public TaskCompletionSource? ScanGate { get; set; }

    /// <summary>Default: the folder is a 64-bit Java home with an unknown version.</summary>
    public Func<string, Result<JavaRuntime>> Inspect { get; set; } = path => TestJava.Manual(path);

    public int ScanCalls { get; private set; }

    public List<string> InspectedPaths { get; } = [];

    public async Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ScanCalls++;
        if (ScanGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return ScanResults.Count > 0 ? ScanResults.Dequeue() : new JavaScanResult([], []);
    }

    public Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default)
    {
        InspectedPaths.Add(path);
        return Task.FromResult(Inspect(path));
    }

    public static JavaScanResult Found(params JavaRuntime[] runtimes) => new(runtimes, []);
}

public static class TestJava
{
    public static readonly JavaRuntime Jdk25 = Discovered(@"C:\Program Files\Eclipse Adoptium\jdk-25", 25, JavaRuntimeSource.Registry);

    public static readonly JavaRuntime Jdk21 = Discovered(@"C:\Program Files\Eclipse Adoptium\jdk-21", 21, JavaRuntimeSource.JavaHome);

    public static JavaRuntimesViewModel ViewModel(ILocalizationService localization, FakeJavaDiscoveryService discovery, IPreferenceStore store) =>
        new(discovery, new FakeFolderPicker(), store, new UserPreferences(null, null, null), new FakePlatformEnvironment(), localization);

    public static JavaRuntime Manual(string home) =>
        new(home, home + @"\bin\javaw.exe", null, null, JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true);

    private static JavaRuntime Discovered(string home, int major, JavaRuntimeSource source) =>
        new(home, home + @"\bin\javaw.exe", new JavaVersion(major, $"{major}.0.1"), "Eclipse Adoptium", source, HasGraalCompiler: false, HasInstrumentLibrary: true);
}

public static class TestInstalls
{
    public static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

    public static readonly PzInstall ManualGame = new(PzInstallKind.Game, @"D:\Games\ProjectZomboid", Steam: null);

    public static readonly PzInstall SteamServer =
        new(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Project Zomboid Dedicated Server", new SteamAppInfo("200", "unstable"));
}

public static class TestErrors
{
    // ViewModels map errors by Code and Details only, so Kind is arbitrary here.
    public static Error Create(ErrorCode code, string? path = null) =>
        new(code, "developer text", ErrorKind.Unexpected)
        {
            Details = path is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["path"] = path },
        };
}

public sealed class FakeFolderPicker : IFolderPicker
{
    public FolderPickResult Next { get; set; } = new FolderPickResult.Cancelled();

    public void PickNext(string path) => Next = new FolderPickResult.Picked(path);

    public Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default) => Task.FromResult(Next);
}

public sealed class FakePreferenceStore : IPreferenceStore
{
    public List<AppLanguage> SavedLanguages { get; } = [];

    public List<AppTheme> SavedThemes { get; } = [];

    public List<IReadOnlyList<string>> SavedManualJavaHomes { get; } = [];

    public List<(string Root, GameLaunchOverrides Overrides)> SavedInstallOverrides { get; } = [];

    public UserPreferences Load() => new(null, null, null);

    public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default)
    {
        SavedLanguages.Add(language);
        return Task.CompletedTask;
    }

    public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default)
    {
        SavedThemes.Add(theme);
        return Task.CompletedTask;
    }

    public Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default)
    {
        SavedManualJavaHomes.Add(homes);
        return Task.CompletedTask;
    }

    /// <summary>Holds install override saves open until completed.</summary>
    public TaskCompletionSource? InstallOverridesSaveGate { get; set; }

    public Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default)
    {
        SavedInstallOverrides.Add((installRoot, overrides));
        return InstallOverridesSaveGate?.Task ?? Task.CompletedTask;
    }

    public List<(string Root, ZombieBuddyChannel Channel)> SavedZombieBuddyChannels { get; } = [];

    public Task SaveZombieBuddyChannelAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct = default)
    {
        SavedZombieBuddyChannels.Add((installRoot, channel));
        return Task.CompletedTask;
    }

    public List<(string Root, string? Name)> SavedDefaultModCollections { get; } = [];

    public Task SaveDefaultModCollectionAsync(string installRoot, string? name, CancellationToken ct = default)
    {
        SavedDefaultModCollections.Add((installRoot, name));
        return Task.CompletedTask;
    }
}

/// <summary>Each successful launch starts a game that runs until <see cref="Exit"/> completes.</summary>
public sealed class FakeGameLaunchService : IGameLaunchService
{
    public Func<string, Result<GameLaunchDefaults>> Defaults { get; set; } = _ => TestLaunch.Defaults();

    public Func<LaunchGameRequest, Result<GameSession>> Launch { get; set; } = request => new GameSession(request.InstallRoot, 4242);

    public Func<LaunchGameRequest, Task<Result>> Validate { get; set; } = _ => Task.FromResult(Result.Ok());

    public List<LaunchGameRequest> Launches { get; } = [];

    public List<LaunchGameRequest> Validations { get; } = [];

    public List<string> DefaultsRequests { get; } = [];

    public TaskCompletionSource<Result> Exit { get; private set; } = Exited();

    public Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default)
    {
        DefaultsRequests.Add(installRoot);
        return Task.FromResult(Defaults(installRoot));
    }

    public Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Launches.Add(request);
        var result = Launch(request);
        if (result.IsSuccess)
        {
            Exit = new TaskCompletionSource<Result>();
        }

        return Task.FromResult(result);
    }

    public Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Validations.Add(request);
        return Validate(request);
    }

    public Task<Result> WaitForExitAsync(CancellationToken ct = default) => Exit.Task.WaitAsync(ct);

    private static TaskCompletionSource<Result> Exited()
    {
        var exited = new TaskCompletionSource<Result>();
        exited.SetResult(Result.Ok());
        return exited;
    }
}

public static class TestLaunch
{
    private static readonly string BundledHome = TestInstalls.SteamGame.RootPath + @"\jre64";

    public static readonly JavaRuntime BundledJava =
        new(BundledHome, BundledHome + @"\bin\javaw.exe", new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true);

    public static GameLaunchDefaults Defaults(int? maxMemoryMb = 3072, int? minMemoryMb = null, params Error[] warnings) =>
        new(maxMemoryMb, minMemoryMb, ["-Xmx3072m", "-XX:+UseZGC"], BundledJava, 16 * 1024, warnings, GarbageCollector.Z, GarbageCollectorSetByViewpoint: false);
}

public sealed class FakeInstallOverridesService : IInstallOverridesService
{
    private readonly Dictionary<string, GameLaunchOverrides> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Root, GameLaunchOverrides Overrides)> Saved { get; } = [];

    public GameLaunchOverrides Get(string installRoot) => _overrides.GetValueOrDefault(installRoot) ?? GameLaunchOverrides.Empty;

    /// <summary>Arranges a saved value without recording a save.</summary>
    public void Set(string installRoot, GameLaunchOverrides overrides) => _overrides[installRoot] = overrides;

    public void Save(string installRoot, GameLaunchOverrides overrides)
    {
        _overrides[installRoot] = overrides;
        Saved.Add((installRoot, overrides));
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// The setup dialog returns <see cref="SetupResult"/>'s answer without running the operation unless the handler starts it.
/// Prompts and confirms take their answers from the queues in order; an empty queue cancels.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    public List<string> Errors { get; } = [];

    public List<ConfirmPrompt> Confirms { get; } = [];

    public List<TextPrompt> Prompts { get; } = [];

    public Queue<bool> ConfirmAnswers { get; } = new();

    public Queue<TextPromptResult> PromptAnswers { get; } = new();

    public List<ZombieBuddySetupViewModel> Setups { get; } = [];

    public Func<ZombieBuddySetupViewModel, Task<bool>> SetupResult { get; set; } = _ => Task.FromResult(true);

    public event Action? Shown;

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        Shown?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        Confirms.Add(prompt);
        Shown?.Invoke();
        return Task.FromResult(ConfirmAnswers.Count > 0 && ConfirmAnswers.Dequeue());
    }

    public Task<TextPromptResult> PromptTextAsync(TextPrompt prompt)
    {
        Prompts.Add(prompt);
        Shown?.Invoke();
        return Task.FromResult(PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : new TextPromptResult.Cancelled());
    }

    public void Accept(string text) => PromptAnswers.Enqueue(new TextPromptResult.Accepted(text));

    public Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup)
    {
        Setups.Add(setup);
        return SetupResult(setup);
    }

    /// <summary>Runs the operation as the real dialog does on open, then answers.</summary>
    public static Func<ZombieBuddySetupViewModel, Task<bool>> RunsSetup(bool launch = true) => async setup =>
    {
        await setup.StartAsync();
        return launch;
    };
}

public sealed class FakeClipboardService : IClipboardService
{
    public string? Text { get; set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);
}

public sealed class FakeModCollectionDefaultsService : IModCollectionDefaultsService
{
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string installRoot) => _defaults.GetValueOrDefault(installRoot);

    public void Save(string installRoot, string name) => _defaults[installRoot] = name;

    public void Clear(string installRoot) => _defaults.Remove(installRoot);
}

/// <summary>
/// An in-memory cache folder: saves, deletes and applies change <see cref="Collections"/> and <see cref="ActiveIds"/> as
/// the real service changes the files, and every call is recorded.
/// </summary>
public sealed class FakeModCollectionService : IModCollectionService
{
    public List<InstalledMod> InstalledMods { get; } = [];

    /// <summary>Name and ids in file order.</summary>
    public List<(string Name, IReadOnlyList<string> ModIds)> Collections { get; } = [];

    public List<string> ActiveIds { get; set; } = [];

    public Func<Error?> GetError { get; set; } = () => null;

    public Func<SaveModCollectionRequest, Error?> SaveError { get; set; } = _ => null;

    public Func<string, Error?> ApplyError { get; set; } = _ => null;

    public List<(string Root, string ExtraGameArgs)> GetRequests { get; } = [];

    public List<SaveModCollectionRequest> Saves { get; } = [];

    public List<string> Deletes { get; } = [];

    public List<(string Root, string ExtraGameArgs, string Name)> Applies { get; } = [];

    public FakeModCollectionService WithMods(params InstalledMod[] mods)
    {
        InstalledMods.AddRange(mods);
        return this;
    }

    public FakeModCollectionService WithCollection(string name, params string[] ids)
    {
        Collections.Add((name, ids));
        return this;
    }

    public IReadOnlyList<string>? IdsOf(string name) =>
        Collections.FindIndex(collection => collection.Name == name) is var index and >= 0 ? Collections[index].ModIds : null;

    public Task<Result<ModCollectionState>> GetAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        GetRequests.Add((installRoot, extraGameArgs));
        if (GetError() is { } error)
        {
            return Task.FromResult<Result<ModCollectionState>>(error);
        }

        var installed = InstalledMods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var active = ActiveIds.Where(installed.Contains).ToHashSet(StringComparer.Ordinal);
        List<ModCollection> collections =
        [
            .. Collections.Select(collection =>
                new ModCollection(collection.Name, collection.ModIds, active.SetEquals(ModLoadOrder.Resolve(collection.ModIds, InstalledMods).EnabledIds))),
        ];
        return Task.FromResult<Result<ModCollectionState>>(new ModCollectionState(
            @"C:\Users\Test\Zomboid", collections, [.. InstalledMods], [.. ActiveIds], active.Count == 0 || collections.Any(collection => collection.IsActive)));
    }

    public Task<Result> SaveAsync(SaveModCollectionRequest request, CancellationToken ct = default)
    {
        Saves.Add(request);
        if (SaveError(request) is { } error)
        {
            return Task.FromResult<Result>(error);
        }

        if (request.OriginalName is { } original && IdsOf(original) is null)
        {
            return Task.FromResult<Result>(TestErrors.Create(ErrorCode.ModCollectionNotFound));
        }

        var target = request.OriginalName ?? request.Name;
        Collections.RemoveAll(collection => collection.Name == request.Name && collection.Name != target);
        var index = Collections.FindIndex(collection => collection.Name == target);
        if (index >= 0)
        {
            Collections[index] = (request.Name, request.ModIds);
        }
        else
        {
            Collections.Add((request.Name, request.ModIds));
        }

        return Task.FromResult(Result.Ok());
    }

    public Task<Result> DeleteAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        Deletes.Add(name);
        return Task.FromResult(Collections.RemoveAll(collection => collection.Name == name) > 0
            ? Result.Ok()
            : TestErrors.Create(ErrorCode.ModCollectionNotFound));
    }

    public Task<Result<ApplyModCollectionResult>> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        Applies.Add((installRoot, extraGameArgs, name));
        if (ApplyError(name) is { } error)
        {
            return Task.FromResult<Result<ApplyModCollectionResult>>(error);
        }

        if (IdsOf(name) is not { } ids)
        {
            return Task.FromResult<Result<ApplyModCollectionResult>>(TestErrors.Create(ErrorCode.ModCollectionNotFound));
        }

        var resolved = ModLoadOrder.Resolve(ids, InstalledMods);
        ActiveIds = [.. resolved.EnabledIds];
        return Task.FromResult<Result<ApplyModCollectionResult>>(resolved);
    }
}

public static class TestMods
{
    public static InstalledMod Mod(
        string id, string? name = null, string? workshopId = null, InstalledModSource source = InstalledModSource.SteamWorkshop,
        string[]? require = null, string[]? incompatible = null) =>
        new(id, name ?? id + " Name", workshopId, source, require ?? [], [], [], incompatible ?? []);
}

/// <summary>Initialize and remove run <see cref="Operation"/>, which by default reports every step and succeeds.</summary>
public sealed class FakeZombieBuddyService : IZombieBuddyService
{
    public Func<string, Result<ZombieBuddyStatus>> Status { get; set; } = _ => ZombieBuddyStatus.Unsupported;

    public Func<IProgress<ZombieBuddyProgress>?, CancellationToken, Task<Result>> Operation { get; set; } =
        (progress, _) => Task.FromResult(TestZombieBuddy.Run(TestZombieBuddy.InitializeSteps, progress));

    public Func<bool, Task<Result>> SetAllowAll { get; set; } = _ => Task.FromResult(Result.Ok());

    public List<string> StatusRequests { get; } = [];

    public List<(string Root, ZombieBuddyChannel Channel)> Initializations { get; } = [];

    public List<string> Removals { get; } = [];

    public List<(string Root, bool AllowAll)> AllowAllChanges { get; } = [];

    public Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default)
    {
        StatusRequests.Add(installRoot);
        return Task.FromResult(Status(installRoot));
    }

    public Task<Result> InitializeAsync(
        string installRoot, ZombieBuddyChannel channel, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        Initializations.Add((installRoot, channel));
        return Operation(progress, ct);
    }

    public Task<Result> RemoveAsync(string installRoot, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        Removals.Add(installRoot);
        return Operation(progress, ct);
    }

    public Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default)
    {
        AllowAllChanges.Add((installRoot, allowAll));
        return SetAllowAll(allowAll);
    }
}

public sealed class FakeZabricService : IZabricService
{
    public Result<ZombieBuddyPreload> PreloadResult { get; set; } = ZombieBuddyPreload.NotApplicable;

    public List<(string Root, string ExtraGameArgs)> Preloads { get; } = [];

    public Task<Result<ZombieBuddyPreload>> EnsurePreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        Preloads.Add((installRoot, extraGameArgs));
        return Task.FromResult(PreloadResult);
    }
}

public sealed class FakeZombieBuddyChannelService : IZombieBuddyChannelService
{
    private readonly Dictionary<string, ZombieBuddyChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Root, ZombieBuddyChannel Channel)> Saved { get; } = [];

    public ZombieBuddyChannel Get(string installRoot) => _channels.GetValueOrDefault(installRoot, ZombieBuddyChannel.Stable);

    /// <summary>Arranges a saved value without recording a save.</summary>
    public void Set(string installRoot, ZombieBuddyChannel channel) => _channels[installRoot] = channel;

    public void Save(string installRoot, ZombieBuddyChannel channel)
    {
        _channels[installRoot] = channel;
        Saved.Add((installRoot, channel));
    }
}

public static class TestZombieBuddy
{
    public static readonly ZombieBuddyItem StableItem =
        new(ZombieBuddyChannel.Stable, @"E:\SteamLibrary\steamapps\workshop\content\108600\3619862853");

    public static readonly ZombieBuddyItem BetaItem =
        new(ZombieBuddyChannel.Beta, @"E:\SteamLibrary\steamapps\workshop\content\108600\3812624292");

    public static readonly ZombieBuddyStep[] InitializeSteps =
        [ZombieBuddyStep.FindItem, ZombieBuddyStep.CopyNativeLoader, ZombieBuddyStep.CopyAgentJar, ZombieBuddyStep.AddAgentArg];

    public static readonly ZombieBuddyStep[] RemoveSteps = [ZombieBuddyStep.DeleteFiles, ZombieBuddyStep.RemoveAgentArg];

    public const string ConfigFolder = @"C:\Users\u\.zombie_buddy";

    public static readonly ZombieBuddyStatus NotSubscribed = new(true, [], false, false, false, ConfigFolder, ZombieBuddyAction.None);

    public static readonly ZombieBuddyStatus Leftovers = new(true, [], false, true, false, ConfigFolder, ZombieBuddyAction.Remove);

    public static ZombieBuddyStatus NotInitialized(params ZombieBuddyItem[] subscribed) =>
        new(true, subscribed.Length == 0 ? [StableItem] : subscribed, false, false, false, ConfigFolder, ZombieBuddyAction.Initialize);

    public static ZombieBuddyStatus Initialized(bool allowAll = false, params ZombieBuddyItem[] subscribed) =>
        new(true, subscribed.Length == 0 ? [StableItem] : subscribed, true, true, allowAll, ConfigFolder, ZombieBuddyAction.None);

    /// <summary>Reports like the real service: each step as it starts, then completion; fails at <paramref name="failAt"/> with <paramref name="error"/>.</summary>
    public static Result Run(IReadOnlyList<ZombieBuddyStep> steps, IProgress<ZombieBuddyProgress>? progress, int? failAt = null, Error? error = null)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            progress?.Report(new ZombieBuddyProgress(steps, i, IsComplete: false));
            if (i == failAt)
            {
                return error ?? TestErrors.Create(ErrorCode.ZombieBuddyCopyFailed, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\ZombieBuddy.jar");
            }
        }

        progress?.Report(new ZombieBuddyProgress(steps, steps.Count, IsComplete: true));
        return Result.Ok();
    }
}

/// <summary>Time stands still until <see cref="Advance"/>; due one-shot timers then fire on the caller's thread.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.ToList())
        {
            timer.FireIfDue();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void FireIfDue()
        {
            if (_due <= owner._now)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class FakeThemeService(AppTheme initial) : IThemeService
{
    public AppTheme Current { get; private set; } = initial;

    public List<AppTheme> Applied { get; } = [];

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        Applied.Add(theme);
    }
}

public sealed class FakeWindowPlacementService : IWindowPlacementService
{
    public int ResetCalls { get; private set; }

    public void ResetToDefault() => ResetCalls++;
}

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public PlatformKind Os { get; set; } = PlatformKind.Windows;

    public string HomeDirectory { get; set; } = @"C:\Users\Test";

    public Version OsVersion { get; set; } = new(10, 0, 26100, 0);

    public long TotalMemoryBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) => null;

    public string? ReadSteamPathFromRegistry() => null;

    public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path) => [];

    public string? ReadRegistryString(RegistryHiveKind hive, string path, string name) => null;
}
