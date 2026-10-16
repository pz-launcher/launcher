using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Java;

namespace PzLauncher.Desktop.ViewModels.VersionSettings;

/// <summary>Launch settings of the install selected on Home; every change is saved at once.</summary>
public partial class VersionSettingsViewModel : ViewModelBase
{
    private const int MemoryStepMb = 512;
    private const int MaxMemoryFloorMb = 2048;
    private const int MinMemoryFloorMb = 512;
    private const int DefaultCustomMaxMemoryMb = 8192;

    private static readonly GarbageCollector[] OfferedCollectors = [GarbageCollector.G1, GarbageCollector.Z, GarbageCollector.Shenandoah];

    private readonly InstallsViewModel _installs;
    private readonly IGameLaunchService _launcher;
    private readonly IInstallOverridesService _overrides;
    private readonly JavaRuntimesViewModel _java;
    private readonly StringComparer _pathComparer;

    // Sources of the shown texts, kept so a language switch can re-render them.
    private readonly List<Error> _warningSources = [];
    private Error? _errorSource;
    private Error? _launchProblemSource;

    // Set while fields are filled from saved values, so filling them doesn't save or pull values around.
    private bool _applying;

    // A newer activation supersedes a load still in flight; a newer check supersedes one still running.
    private int _loadVersion;
    private int _validationVersion;

    // A saved min above the effective max stays in the min slider's range until a max change pulls it down.
    private int _loadedMinMemoryMb;

    public VersionSettingsViewModel(
        InstallsViewModel installs,
        IGameLaunchService launcher,
        IInstallOverridesService overrides,
        JavaRuntimesViewModel java,
        IPlatformEnvironment platform,
        ILocalizationService localization)
    {
        _installs = installs;
        _launcher = launcher;
        _overrides = overrides;
        _java = java;
        _pathComparer = StringComparer.FromComparison(platform.PathComparison);

        // Never unsubscribed: this view model is an app-lifetime singleton.
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    public event EventHandler? BackRequested;

    // Slider step for the view, typed like RangeBase's properties.
    public static double MemoryStep => MemoryStepMb;

    [ObservableProperty] public partial PzInstall? Install { get; private set; }

    [ObservableProperty] public partial bool IsLoading { get; private set; }

    /// <summary>The settings card is shown: the install's defaults were read (a bad json is only a warning).</summary>
    [ObservableProperty] public partial bool HasSettings { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <summary>Why the next launch would fail with the current settings, other than a GC problem.</summary>
    [ObservableProperty] public partial string? LaunchProblemText { get; private set; }

    /// <summary>Why the next launch would fail because of the GC; shown under the GC choice.</summary>
    [ObservableProperty] public partial string? GcProblemText { get; private set; }

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<JavaOption> JavaOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaSelection), nameof(ShowJavaVersionWarning))]
    public partial JavaOption? SelectedJava { get; private set; }

    // The combo box pushes null while its items are replaced; that must not clear the choice.
    public JavaOption? JavaSelection
    {
        get => SelectedJava;
        set
        {
            if (value is not null)
            {
                SelectedJava = value;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowJavaVersionWarning))]
    public partial int? BundledJavaMajor { get; private set; }

    /// <summary>The chosen Java is older than the bundled one (both versions known). Launch is still allowed.</summary>
    public bool ShowJavaVersionWarning => SelectedJava?.Runtime?.Version?.Major < BundledJavaMajor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveMaxMemoryMb), nameof(MinMemorySliderMaximum))]
    public partial int? JsonMaxMemoryMb { get; private set; }

    [ObservableProperty] public partial int? JsonMinMemoryMb { get; private set; }

    [ObservableProperty] public partial string JsonVmArgs { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemMemoryGb), nameof(EffectiveMaxMemoryMb), nameof(MinMemorySliderMaximum))]
    public partial int SystemMemoryMb { get; private set; }

    public double SystemMemoryGb => Math.Round(SystemMemoryMb / 1024.0, 1);

    private int SystemMemoryLimitMb => Math.Max(MaxMemoryFloorMb, SystemMemoryMb / MemoryStepMb * MemoryStepMb);

    // Slider ranges also span the values loaded for the install: a slider clamps a value outside its range and writes it
    // back, which would rewrite a saved value (another machine's memory, a hand-edited file) just by opening the page.
    [ObservableProperty] public partial int MaxMemorySliderMinimum { get; private set; } = MaxMemoryFloorMb;

    [ObservableProperty] public partial int MaxMemorySliderMaximum { get; private set; } = MaxMemoryFloorMb;

    [ObservableProperty] public partial int MinMemorySliderMinimum { get; private set; } = MinMemoryFloorMb;

    [ObservableProperty] public partial MemoryMode MaxMemoryMode { get; set; }

    [ObservableProperty] public partial int CustomMaxMemoryMb { get; set; }

    public double CustomMaxMemoryGb => CustomMaxMemoryMb / 1024.0;

    /// <summary>The <c>-Xmx</c> the next launch uses; the machine's limit when the json sets none.</summary>
    public int EffectiveMaxMemoryMb => MaxMemoryMode == MemoryMode.Custom ? CustomMaxMemoryMb : JsonMaxMemoryMb ?? SystemMemoryLimitMb;

    public int MinMemorySliderMaximum => Math.Max(Math.Max(MinMemoryFloorMb, EffectiveMaxMemoryMb), _loadedMinMemoryMb);

    [ObservableProperty] public partial MemoryMode MinMemoryMode { get; set; }

    [ObservableProperty] public partial int CustomMinMemoryMb { get; set; }

    public double CustomMinMemoryGb => CustomMinMemoryMb / 1024.0;

    [ObservableProperty] public partial string ExtraJvmArgs { get; set; } = "";

    [ObservableProperty] public partial string ExtraGameArgs { get; set; } = "";

    public ObservableCollection<GcOption> GcOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GcSelection))]
    public partial GcOption? SelectedGc { get; private set; }

    // The combo box pushes null while its items are replaced; that must not clear the choice.
    public GcOption? GcSelection
    {
        get => SelectedGc;
        set
        {
            if (value is not null)
            {
                SelectedGc = value;
            }
        }
    }

    private bool IsEditing => !_applying && HasSettings && Install is not null;

    // The view calls this on every load: the selected install may have changed since.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ActivateAsync(CancellationToken ct)
    {
        var load = ++_loadVersion;
        var install = _installs.SelectedInstall is { Kind: PzInstallKind.Game } game ? game : null;
        Install = install;
        HasSettings = false;
        ShowError(null);
        _validationVersion++;
        ShowLaunchProblem(null);
        _warningSources.Clear();
        Warnings.Clear();
        if (install is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var defaults = _launcher.GetDefaultsAsync(install.RootPath, ct);
            await _java.EnsureScannedAsync();
            var result = await defaults;
            if (load != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                ShowError(result.Error);
                return;
            }

            Apply(result.Value, _overrides.Get(install.RootPath));
            HasSettings = true;
            await ValidateAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled: nothing to show.
        }
        finally
        {
            if (load == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    private void Apply(GameLaunchDefaults defaults, GameLaunchOverrides saved)
    {
        _applying = true;
        try
        {
            SystemMemoryMb = defaults.SystemMemoryMb;
            JsonMaxMemoryMb = defaults.MaxMemoryMb;
            JsonMinMemoryMb = defaults.MinMemoryMb;
            JsonVmArgs = string.Join(" ", defaults.VmArgs);
            BundledJavaMajor = defaults.BundledJava?.Version?.Major;
            foreach (var warning in defaults.Warnings)
            {
                _warningSources.Add(warning);
                Warnings.Add(ErrorMessages.ToUserText(warning));
            }

            RebuildJavaOptions(defaults.BundledJava, saved.JavaHome);

            // Ranges before values, so a bound slider never sees a loaded value outside its range.
            MaxMemorySliderMinimum = Math.Min(MaxMemoryFloorMb, saved.MaxMemoryMb ?? MaxMemoryFloorMb);
            MaxMemorySliderMaximum = Math.Max(SystemMemoryLimitMb, saved.MaxMemoryMb ?? 0);
            MinMemorySliderMinimum = Math.Min(MinMemoryFloorMb, saved.MinMemoryMb ?? MinMemoryFloorMb);
            _loadedMinMemoryMb = saved.MinMemoryMb ?? 0;
            OnPropertyChanged(nameof(MinMemorySliderMaximum));

            MaxMemoryMode = saved.MaxMemoryMb is null ? MemoryMode.FollowJson : MemoryMode.Custom;
            CustomMaxMemoryMb = saved.MaxMemoryMb
                ?? Math.Clamp(JsonMaxMemoryMb ?? DefaultCustomMaxMemoryMb, MaxMemoryFloorMb, SystemMemoryLimitMb);
            MinMemoryMode = saved.MinMemoryMb is null ? MemoryMode.FollowJson : MemoryMode.Custom;
            CustomMinMemoryMb = saved.MinMemoryMb
                ?? Math.Clamp(JsonMinMemoryMb ?? MinMemoryFloorMb, MinMemoryFloorMb, MinMemorySliderMaximum);

            ExtraJvmArgs = saved.ExtraJvmArgs;
            ExtraGameArgs = saved.ExtraGameArgs;
            RebuildGcOptions(defaults, saved.GarbageCollector);
        }
        finally
        {
            _applying = false;
        }
    }

    private void RebuildJavaOptions(JavaRuntime? bundled, string? savedHome)
    {
        JavaOptions.Clear();
        JavaOptions.Add(JavaOption.Bundled(bundled));
        foreach (var runtime in _java.AvailableRuntimes)
        {
            JavaOptions.Add(JavaOption.Available(runtime));
        }

        JavaOption selected;
        if (savedHome is null)
        {
            selected = JavaOptions[0];
        }
        else if (JavaOptions.FirstOrDefault(option => option.HomePath is { } home && _pathComparer.Equals(home, savedHome)) is { } match)
        {
            selected = match;
        }
        else
        {
            selected = JavaOption.Missing(savedHome);
            JavaOptions.Add(selected);
        }

        SelectedJava = selected;
    }

    private void RebuildGcOptions(GameLaunchDefaults defaults, GarbageCollector? saved)
    {
        GcOptions.Clear();
        GcOptions.Add(GcOption.FollowJson(defaults.GarbageCollector, defaults.GarbageCollectorSetByViewpoint));
        foreach (var collector in OfferedCollectors)
        {
            GcOptions.Add(GcOption.Override(collector));
        }

        // A collector the page doesn't offer (a hand-edited file) is listed so the selection stays visible.
        if (saved is { } kept && !OfferedCollectors.Contains(kept))
        {
            GcOptions.Add(GcOption.Override(kept));
        }

        SelectedGc = GcOptions.First(option => option.Collector == saved);
    }

    partial void OnSelectedJavaChanged(JavaOption? value) => SaveAndValidate();

    partial void OnSelectedGcChanged(GcOption? value) => SaveAndValidate();

    partial void OnMaxMemoryModeChanged(MemoryMode value) => OnMaxMemoryChanged();

    partial void OnCustomMaxMemoryMbChanged(int value)
    {
        OnPropertyChanged(nameof(CustomMaxMemoryGb));
        OnMaxMemoryChanged();
    }

    partial void OnMinMemoryModeChanged(MemoryMode value) => Save();

    partial void OnCustomMinMemoryMbChanged(int value)
    {
        OnPropertyChanged(nameof(CustomMinMemoryGb));
        Save();
    }

    partial void OnExtraJvmArgsChanged(string value) => SaveAndValidate();

    partial void OnExtraGameArgsChanged(string value) => Save();

    private void OnMaxMemoryChanged()
    {
        OnPropertyChanged(nameof(EffectiveMaxMemoryMb));
        if (_applying)
        {
            OnPropertyChanged(nameof(MinMemorySliderMaximum));
            return;
        }

        // Keeps -Xms <= -Xmx, which the launch rejects otherwise.
        var effectiveMin = MinMemoryMode == MemoryMode.Custom ? CustomMinMemoryMb : JsonMinMemoryMb;
        if (effectiveMin > EffectiveMaxMemoryMb)
        {
            _applying = true;
            try
            {
                CustomMinMemoryMb = EffectiveMaxMemoryMb;
                MinMemoryMode = MemoryMode.Custom;
            }
            finally
            {
                _applying = false;
            }
        }

        // After the pull-down, so the min slider's range shrinks around a value it already holds.
        _loadedMinMemoryMb = 0;
        OnPropertyChanged(nameof(MinMemorySliderMaximum));
        Save();
    }

    private GameLaunchOverrides CurrentOverrides() => new(
        SelectedJava?.HomePath,
        MaxMemoryMode == MemoryMode.Custom ? CustomMaxMemoryMb : null,
        MinMemoryMode == MemoryMode.Custom ? CustomMinMemoryMb : null,
        ExtraJvmArgs,
        ExtraGameArgs,
        SelectedGc?.Collector);

    private void Save()
    {
        if (IsEditing && Install is not null)
        {
            _overrides.Save(Install.RootPath, CurrentOverrides());
        }
    }

    // Memory changes aren't checked: the page keeps min <= max itself, and the GC rules don't depend on memory.
    private void SaveAndValidate()
    {
        if (!IsEditing)
        {
            return;
        }

        Save();
        _ = ValidateAsync(CancellationToken.None);
    }

    private async Task ValidateAsync(CancellationToken ct)
    {
        if (Install is not { } install)
        {
            return;
        }

        var version = ++_validationVersion;
        var result = await _launcher.ValidateAsync(new LaunchGameRequest(install.RootPath, CurrentOverrides()), ct);
        if (version != _validationVersion)
        {
            return;
        }

        // An unreadable json is already shown as a warning.
        ShowLaunchProblem(result.IsSuccess || _warningSources.Any(warning => warning.Code == result.Error.Code) ? null : result.Error);
    }

    private void ShowError(Error? error)
    {
        _errorSource = error;
        ErrorText = error is null ? null : ErrorMessages.ToUserText(error);
    }

    private void ShowLaunchProblem(Error? problem)
    {
        _launchProblemSource = problem;
        var text = problem is null ? null : ErrorMessages.ToUserText(problem);
        var isGc = problem?.Code is ErrorCode.LaunchGcIncompatible or ErrorCode.LaunchGcIncompatibleViewpoint or ErrorCode.LaunchGcConflict;
        GcProblemText = isGc ? text : null;
        LaunchProblemText = isGc ? null : text;
    }

    private void RenderTexts()
    {
        ErrorText = _errorSource is null ? null : ErrorMessages.ToUserText(_errorSource);
        ShowLaunchProblem(_launchProblemSource);
        for (var i = 0; i < _warningSources.Count; i++)
        {
            Warnings[i] = ErrorMessages.ToUserText(_warningSources[i]);
        }
    }
}
