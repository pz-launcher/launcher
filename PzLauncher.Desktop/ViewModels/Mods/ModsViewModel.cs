using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The mods page of the game install in use: both sections show one loaded state, and every write re-reads it.</summary>
public partial class ModsViewModel : ViewModelBase
{
    private static readonly TimeSpan OrderSaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly InstallsViewModel _installs;
    private readonly IInstallOverridesService _overrides;
    private readonly IDialogService _dialogs;
    private readonly GameRunState _runState;
    private readonly TimeProvider _time;

    private Func<string>? _errorSource;
    private bool _activated;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    // A reorder waits so a drag writes once; it carries its install so a later install switch can't redirect it.
    private PendingOrder? _pendingOrder;
    private CancellationTokenSource? _pendingOrderDelay;

    public ModsViewModel(
        InstallsViewModel installs,
        IModCollectionService service,
        IInstallOverridesService overrides,
        IModCollectionDefaultsService defaults,
        ModCollectionApplier applier,
        IDialogService dialogs,
        IClipboardService clipboard,
        GameRunState runState,
        ILocalizationService localization,
        TimeProvider time)
    {
        _installs = installs;
        Service = service;
        _overrides = overrides;
        _dialogs = dialogs;
        _runState = runState;
        _time = time;
        Mods = new InstalledModsViewModel(this);
        Collections = new ModCollectionsViewModel(this, defaults, applier, dialogs, clipboard);

        // Never unsubscribed: these are app-lifetime singletons.
        localization.LanguageChanged += (_, _) => RenderTexts();
        installs.PropertyChanged += OnInstallsPropertyChanged;
        runState.PropertyChanged += OnRunStatePropertyChanged;
    }

    public InstalledModsViewModel Mods { get; }

    public ModCollectionsViewModel Collections { get; }

    [ObservableProperty] public partial ModsSection Section { get; set; }

    /// <summary><c>null</c> when the install in use is none or a dedicated server.</summary>
    public PzInstall? Install => _installs.SelectedInstall is { Kind: PzInstallKind.Game } install ? install : null;

    public bool HasNoGameInstall => Install is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanEdit))]
    public partial ModCollectionState? State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <inheritdoc cref="GameRunState.IsGameRunning"/>
    public bool IsGameRunning => _runState.IsGameRunning;

    /// <summary>The game's files may be written now.</summary>
    public bool CanWrite => CanEdit && !IsLoading && !IsBusy;

    /// <summary>A reorder may be queued: it is written once nothing else is.</summary>
    internal bool CanEdit => Install is not null && State is not null && !IsGameRunning;

    internal IModCollectionService Service { get; }

    // The view calls this on every load: the files may have changed in-game since the last visit.
    [RelayCommand]
    private Task ActivateAsync()
    {
        _activated = true;
        return ReloadAsync();
    }

    private bool CanRefresh() => Install is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ReloadAsync();

    /// <summary>Writes a reorder still waiting for its delay now; a running game keeps it until it exits.</summary>
    public async Task FlushOrderAsync()
    {
        // A write in flight saves it before its own change.
        if (_pendingOrder is null || IsBusy || IsGameRunning)
        {
            return;
        }

        IsBusy = true;
        Result result;
        try
        {
            result = await SavePendingOrderAsync();
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
        }

        await ReloadAsync();
    }

    internal async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        if (Install is not { } install)
        {
            State = null;
            ShowError(null);
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var result = await Service.GetAsync(install.RootPath, ExtraGameArgs(install));
            if (version != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                var error = result.Error;
                State = null;
                ShowError(() => ErrorMessages.ToUserText(error));
                return;
            }

            ShowError(null);
            State = result.Value;
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Runs one change of the shown install's files after any queued reorder, then re-reads them. False when it couldn't start.</summary>
    internal async Task<bool> RunAsync(Func<string, string, Task> action)
    {
        if (!CanWrite || Install is not { } install)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var flushed = await SavePendingOrderAsync();
            if (!flushed.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(flushed.Error));
                return false;
            }

            await action(install.RootPath, ExtraGameArgs(install));
            return true;
        }
        finally
        {
            IsBusy = false;
            await ReloadAsync();
        }
    }

    /// <summary><see cref="RunAsync"/> for one service write; a failure is shown. True when it was written.</summary>
    internal async Task<bool> WriteAsync(Func<string, string, Task<Result>> write)
    {
        var written = false;
        await RunAsync(async (root, extraGameArgs) =>
        {
            var result = await write(root, extraGameArgs);
            written = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
            }
        });
        return written;
    }

    internal void ScheduleOrderSave(string name, IReadOnlyList<string> modIds)
    {
        if (Install is not { } install)
        {
            return;
        }

        _pendingOrder = new PendingOrder(install.RootPath, ExtraGameArgs(install), name, modIds);
        CancelOrderDelay();
        var delay = new CancellationTokenSource();
        _pendingOrderDelay = delay;
        _ = SaveOrderLaterAsync(delay.Token);
    }

    /// <summary>The queued order of this collection of the shown install, which a reload must not undo.</summary>
    internal IReadOnlyList<string>? PendingOrderOf(string name) =>
        _pendingOrder is { } pending && pending.Name == name && Install?.RootPath == pending.InstallRoot
            ? pending.ModIds
            : null;

    private async Task SaveOrderLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(OrderSaveDelay, _time, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FlushOrderAsync();
    }

    private async Task<Result> SavePendingOrderAsync()
    {
        if (_pendingOrder is not { } pending)
        {
            return Result.Ok();
        }

        _pendingOrder = null;
        CancelOrderDelay();
        return await Service.SaveAsync(
            new SaveModCollectionRequest(pending.InstallRoot, pending.ExtraGameArgs, pending.Name, pending.Name, pending.ModIds));
    }

    private void CancelOrderDelay()
    {
        _pendingOrderDelay?.Cancel();
        _pendingOrderDelay?.Dispose();
        _pendingOrderDelay = null;
    }

    private string ExtraGameArgs(PzInstall install) => _overrides.Get(install.RootPath).ExtraGameArgs;

    private void ShowError(Func<string>? source)
    {
        _errorSource = source;
        ErrorText = source?.Invoke();
    }

    private void RenderTexts()
    {
        ErrorText = _errorSource?.Invoke();
        Collections.RenderTexts();
    }

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(InstallsViewModel.SelectedInstall))
        {
            return;
        }

        State = null;
        ShowError(null);
        OnPropertyChanged(nameof(Install));
        OnPropertyChanged(nameof(HasNoGameInstall));
        OnPropertyChanged(nameof(CanWrite));
        RefreshCommand.NotifyCanExecuteChanged();
        if (_activated)
        {
            // Fire and forget: the load reports its own errors.
            _ = ReloadAsync();
        }
    }

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(GameRunState.IsGameRunning))
        {
            return;
        }

        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(CanWrite));
        if (!IsGameRunning && _activated)
        {
            // The game may have rewritten the files; a reorder queued before it started is written now.
            _ = _pendingOrder is null ? ReloadAsync() : FlushOrderAsync();
        }
    }

    private sealed record PendingOrder(string InstallRoot, string ExtraGameArgs, string Name, IReadOnlyList<string> ModIds);
}
