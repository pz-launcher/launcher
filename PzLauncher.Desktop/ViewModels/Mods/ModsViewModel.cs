using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The mods page of the game install in use: the game-file sections share one loaded state that every write re-reads; the Workshop sections load their own.</summary>
public partial class ModsViewModel : ViewModelBase
{
    private readonly InstallsViewModel _installs;
    private readonly IInstallOverridesService _overrides;
    private readonly IDialogService _dialogs;
    private readonly GameRunState _runState;
    private readonly IModCollectionOrderWriter _orderWriter;

    private readonly LocalizedText _error;
    private bool _activated;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    public ModsViewModel(
        InstallsViewModel installs,
        IModCollectionService service,
        IInstallOverridesService overrides,
        IModCollectionDefaultsService defaults,
        ModCollectionApplier applier,
        IDialogService dialogs,
        IClipboardService clipboard,
        GameRunState runState,
        IModCollectionOrderWriter orderWriter,
        IWorkshopService workshop,
        IWorkshopActionRunner workshopActions,
        IUrlLauncher urls,
        WorkshopCollectionHistory workshopHistory,
        ILocalizationService localization)
    {
        _installs = installs;
        Service = service;
        _overrides = overrides;
        _dialogs = dialogs;
        _runState = runState;
        _orderWriter = orderWriter;
        Mods = new InstalledModsViewModel(this);
        Collections = new ModCollectionsViewModel(this, defaults, applier, dialogs, clipboard);
        WorkshopSubscriptions = new WorkshopSubscriptionsViewModel(this, workshop, workshopActions, dialogs, clipboard, urls);
        WorkshopCollections = new WorkshopCollectionsViewModel(this, workshop, workshopActions, dialogs, urls, workshopHistory);
        _error = new LocalizedText(text => ErrorText = text);

        // Never unsubscribed: these are app-lifetime singletons.
        localization.LanguageChanged += (_, _) => RenderTexts();
        installs.PropertyChanged += OnInstallsPropertyChanged;
        runState.PropertyChanged += OnRunStatePropertyChanged;
        orderWriter.Written += (_, result) => _ = ReportOrderWrittenAsync(result);
    }

    public InstalledModsViewModel Mods { get; }

    public ModCollectionsViewModel Collections { get; }

    public WorkshopSubscriptionsViewModel WorkshopSubscriptions { get; }

    public WorkshopCollectionsViewModel WorkshopCollections { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGameFiles))]
    public partial ModsSection Section { get; set; }

    /// <summary>The section shows the game's mod files, whose load state and error are the page's own.</summary>
    public bool ShowsGameFiles => Section switch
    {
        ModsSection.Mods or ModsSection.Collections => true,
        ModsSection.WorkshopSubscriptions or ModsSection.WorkshopCollections => false,
    };

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
        return Task.WhenAll(ReloadAsync(), ShowWorkshopSectionAsync());
    }

    private bool CanRefresh() => Install is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => Section switch
    {
        ModsSection.Mods or ModsSection.Collections => ReloadAsync(),
        ModsSection.WorkshopSubscriptions => WorkshopSubscriptions.LoadAsync(),
        ModsSection.WorkshopCollections => WorkshopCollections.RefreshAsync(),
    };

    // Steam may have changed the subscriptions since the last visit.
    partial void OnSectionChanged(ModsSection value)
    {
        if (_activated)
        {
            _ = ShowWorkshopSectionAsync();
        }
    }

    private Task ShowWorkshopSectionAsync() => Section switch
    {
        ModsSection.Mods or ModsSection.Collections => Task.CompletedTask,
        ModsSection.WorkshopSubscriptions => WorkshopSubscriptions.LoadAsync(),
        ModsSection.WorkshopCollections => WorkshopCollections.ShowAsync(),
    };

    internal async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        if (Install is not { } install)
        {
            State = null;
            _error.Clear();
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
                State = null;
                _error.Show(result.Error);
                return;
            }

            _error.Clear();
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
            var flushed = await _orderWriter.FlushAsync();
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

        // The queued write keeps its install, so a later install switch can't redirect it.
        _orderWriter.Queue(install.RootPath, ExtraGameArgs(install), name, modIds);
    }

    /// <summary>The queued order of this collection of the shown install, which a reload must not undo.</summary>
    internal IReadOnlyList<string>? PendingOrderOf(string name) =>
        Install is { } install ? _orderWriter.PendingOrder(install.RootPath, name) : null;

    private async Task ReportOrderWrittenAsync(Result result)
    {
        if (!result.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
        }

        await ReloadAsync();
    }

    internal string ExtraGameArgs(PzInstall install) => _overrides.Get(install.RootPath).ExtraGameArgs;

    private void RenderTexts()
    {
        _error.Render();
        Collections.RenderTexts();
        WorkshopSubscriptions.RenderTexts();
        WorkshopCollections.RenderTexts();
    }

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(InstallsViewModel.SelectedInstall))
        {
            return;
        }

        State = null;
        _error.Clear();
        OnPropertyChanged(nameof(Install));
        OnPropertyChanged(nameof(HasNoGameInstall));
        OnPropertyChanged(nameof(CanWrite));
        RefreshCommand.NotifyCanExecuteChanged();
        WorkshopSubscriptions.Clear();
        if (_activated)
        {
            // Fire and forget: the loads report their own errors.
            _ = ReloadAsync();
            _ = ShowWorkshopSectionAsync();
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
            // The game may have rewritten the files.
            _ = ReloadAsync();
        }
    }
}
