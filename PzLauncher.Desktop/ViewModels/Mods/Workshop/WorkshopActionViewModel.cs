using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// One per progress dialog: the dialog starts the Steam action when it opens, closing it cancels a run in progress, and at
/// the end it stays open as a summary.
/// </summary>
/// <param name="observer">Sees every event the helper reports, as the dialog applies it.</param>
/// <param name="stillOutdated">After an update, the ids whose installed copy still isn't the latest version.</param>
public sealed partial class WorkshopActionViewModel(
    IWorkshopActionRunner runner,
    string installRoot,
    WorkshopAction action,
    IReadOnlyList<WorkshopActionItemRow> items,
    Action<WorkshopActionEvent>? observer = null,
    Func<CancellationToken, Task<IReadOnlySet<ulong>>>? stillOutdated = null) : DialogOperationViewModel
{
    public WorkshopAction Action { get; } = action;

    public string Title => Action switch
    {
        WorkshopAction.Subscribe => UiText.Workshop_ActionSubscribeTitle,
        WorkshopAction.Unsubscribe => UiText.Workshop_ActionUnsubscribeTitle,
        WorkshopAction.Update => UiText.Workshop_ActionUpdateTitle,
    };

    public IReadOnlyList<WorkshopActionItemRow> Items { get; } = items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForSteam), nameof(ShowsWaitingForSteam))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForSteam), nameof(ShowsWaitingForSteam))]
    public partial bool IsSteamStarted { get; private set; }

    /// <summary>An earlier Steam action from this launcher still runs; this one starts after it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWaitingForSteam))]
    public partial bool IsWaitingForEarlierAction { get; private set; }

    /// <summary>Started, and Steam hasn't accepted it yet.</summary>
    public bool IsWaitingForSteam => IsRunning && !IsSteamStarted;

    /// <summary>Waiting for Steam, and not for an earlier action first.</summary>
    public bool ShowsWaitingForSteam => IsWaitingForSteam && !IsWaitingForEarlierAction;

    [ObservableProperty] public partial bool IsFinished { get; private set; }

    /// <summary>0 to 1: the share of items with a result.</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    [ObservableProperty] public partial string? SummaryText { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    protected override async Task RunAsync()
    {
        IsRunning = true;
        var run = await RunWithProgressAsync<WorkshopActionEvent, Result<WorkshopActionOutcome>>(
            (progress, ct) => runner.RunAsync(installRoot, new WorkshopActionRequest(Action, [.. Items.Select(item => item.Id)]), progress, ct),
            OnEvent);
        if (run is not { } result)
        {
            return;
        }

        if (result.IsSuccess)
        {
            var rows = Items.ToDictionary(item => item.Id);
            foreach (var outcome in result.Value.Items)
            {
                if (rows.TryGetValue(outcome.Id, out var row))
                {
                    row.Finish(outcome);
                }
            }

            if (stillOutdated is not null && Items.Any(item => item.State == WorkshopActionItemState.Done))
            {
                var ct = AbortToken;
                IReadOnlySet<ulong> outdated;
                try
                {
                    outdated = await stillOutdated(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                foreach (var row in Items.Where(item => item.State == WorkshopActionItemState.Done && outdated.Contains(item.Id)))
                {
                    row.MarkStillOutdated();
                }
            }
        }
        else
        {
            ErrorText = ErrorMessages.ToUserText(result.Error);
        }

        var done = Items.Count(item => item.State == WorkshopActionItemState.Done);
        IsWaitingForEarlierAction = false;
        Progress = 1;
        SummaryText = string.Format(CultureInfo.CurrentCulture, UiText.Workshop_ActionSummary, done, Items.Count);
        IsRunning = false;
        IsFinished = true;
    }

    [RelayCommand]
    private void Close() => AbortAndClose();

    private void OnEvent(WorkshopActionEvent report)
    {
        IsWaitingForEarlierAction = report is WorkshopActionWaiting;
        switch (report)
        {
            case WorkshopActionStarted:
                IsSteamStarted = true;
                break;
            case WorkshopItemProgress itemProgress:
                Items.FirstOrDefault(item => item.Id == itemProgress.Id)?.ShowProgress(itemProgress.Bytes, itemProgress.Total);
                break;
            case WorkshopItemFinished itemFinished:
                Items.FirstOrDefault(item => item.Id == itemFinished.Outcome.Id)?.Finish(itemFinished.Outcome);
                Progress = Items.Count == 0 ? 0 : (double)Items.Count(item => item.State is WorkshopActionItemState.Done or WorkshopActionItemState.Failed) / Items.Count;
                break;
        }

        observer?.Invoke(report);
    }
}
