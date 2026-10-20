using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.ViewModels.ZombieBuddy;

/// <summary>One per dialog: closing it any way other than the launch button cancels; the result is <c>true</c> when the game should launch now.</summary>
public sealed partial class ZombieBuddySetupViewModel(
    IZombieBuddyService service, ZombieBuddySetupMode mode, string installRoot, ZombieBuddyChannel channel, bool offersLaunch) : DialogOperationViewModel
{
    private readonly IZombieBuddyService _service = service;
    private readonly string _installRoot = installRoot;
    private readonly ZombieBuddyChannel _channel = channel;

    public ZombieBuddySetupMode Mode { get; } = mode;

    /// <summary>Success offers launching the game (Home); otherwise only confirming (launch settings).</summary>
    public bool OffersLaunch { get; } = offersLaunch;

    // Once a close-only dialog has succeeded there is nothing left to cancel, and 取消 would read as undoing it.
    public bool OffersConfirm => !OffersLaunch && IsSucceeded;

    public bool OffersCancel => !OffersConfirm;

    public string Title => Mode switch
    {
        ZombieBuddySetupMode.Initialize => UiText.ZombieBuddy_InitializeTitle,
        ZombieBuddySetupMode.Remove => UiText.ZombieBuddy_RemoveTitle,
    };

    public string Intro => Mode switch
    {
        ZombieBuddySetupMode.Initialize => UiText.ZombieBuddy_InitializeIntro,
        ZombieBuddySetupMode.Remove => UiText.ZombieBuddy_RemoveIntro,
    };

    /// <summary>Filled from the first progress report, which lists every step.</summary>
    public ObservableCollection<ZombieBuddyStepRow> Steps { get; } = [];

    /// <summary>0 to 1.</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFailed), nameof(OffersConfirm), nameof(OffersCancel))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand), nameof(ConfirmCommand), nameof(RetryCommand))]
    public partial bool IsSucceeded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsRunning { get; private set; }

    public bool IsFailed => !IsRunning && !IsSucceeded && IsStarted;

    protected override async Task RunAsync()
    {
        Steps.Clear();
        Progress = 0;
        ErrorText = null;
        IsRunning = true;
        var run = await RunWithProgressAsync<ZombieBuddyProgress, Result>(
            (progress, ct) => Mode switch
            {
                ZombieBuddySetupMode.Initialize => _service.InitializeAsync(_installRoot, _channel, progress, ct),
                ZombieBuddySetupMode.Remove => _service.RemoveAsync(_installRoot, progress, ct),
            },
            OnProgress);
        if (run is not { } result)
        {
            return;
        }

        if (result.IsSuccess)
        {
            foreach (var row in Steps)
            {
                row.State = ZombieBuddyStepState.Done;
            }

            Progress = 1;
            IsSucceeded = true;
        }
        else
        {
            foreach (var row in Steps)
            {
                if (row.State == ZombieBuddyStepState.Running)
                {
                    row.State = ZombieBuddyStepState.Failed;
                }
            }

            ErrorText = ErrorMessages.ToUserText(result.Error);
        }

        IsRunning = false;
    }

    [RelayCommand(CanExecute = nameof(IsSucceeded))]
    private void Launch() => RequestClose(true);

    [RelayCommand(CanExecute = nameof(IsSucceeded))]
    private void Confirm() => RequestClose(false);

    [RelayCommand]
    private void Cancel() => AbortAndClose();

    // Every step is idempotent, so a retry runs the whole operation again.
    [RelayCommand(CanExecute = nameof(IsFailed))]
    private Task RetryAsync() => RunAsync();

    private void OnProgress(ZombieBuddyProgress report)
    {
        if (Steps.Count != report.Steps.Count)
        {
            Steps.Clear();
            foreach (var step in report.Steps)
            {
                Steps.Add(new ZombieBuddyStepRow(step));
            }
        }

        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].State = i < report.Current ? ZombieBuddyStepState.Done
                : i == report.Current && !report.IsComplete ? ZombieBuddyStepState.Running
                : ZombieBuddyStepState.Pending;
        }

        Progress = Steps.Count == 0 ? 0 : Math.Min(1, (double)report.Current / Steps.Count);
    }
}
