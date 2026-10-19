using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Shell;

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
    Func<CancellationToken, Task<IReadOnlySet<ulong>>>? stillOutdated = null) : ViewModelBase
{
    private readonly CancellationTokenSource _cancel = new();
    private bool _started;

    public event EventHandler? CloseRequested;

    public WorkshopAction Action { get; } = action;

    public string Title => Action switch
    {
        WorkshopAction.Subscribe => UiText.Workshop_ActionSubscribeTitle,
        WorkshopAction.Unsubscribe => UiText.Workshop_ActionUnsubscribeTitle,
        WorkshopAction.Update => UiText.Workshop_ActionUpdateTitle,
    };

    public IReadOnlyList<WorkshopActionItemRow> Items { get; } = items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForSteam))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForSteam))]
    public partial bool IsSteamStarted { get; private set; }

    /// <summary>The helper runs, but Steam hasn't accepted it yet.</summary>
    public bool IsWaitingForSteam => IsRunning && !IsSteamStarted;

    [ObservableProperty] public partial bool IsFinished { get; private set; }

    /// <summary>0 to 1: the share of items with a result.</summary>
    [ObservableProperty] public partial double Progress { get; private set; }

    [ObservableProperty] public partial string? SummaryText { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <summary>Runs once; later calls return at once.</summary>
    public Task StartAsync()
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
        return RunAsync();
    }

    /// <summary>Stops a run in progress; for a dialog closed by the window's own close button.</summary>
    public void Abort() => _cancel.Cancel();

    [RelayCommand]
    private void Close()
    {
        Abort();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync()
    {
        var ct = _cancel.Token;
        IsRunning = true;

        // Reports posted before the result arrives are applied before it; anything later is ignored.
        var finished = false;
        var progress = new ContextProgress<WorkshopActionEvent>(report =>
        {
            if (!finished)
            {
                OnEvent(report);
            }
        });

        Result<WorkshopActionOutcome> result;
        try
        {
            result = await runner.RunAsync(installRoot, new WorkshopActionRequest(Action, [.. Items.Select(item => item.Id)]), progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The dialog is closing.
            return;
        }
        finally
        {
            finished = true;
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
        Progress = 1;
        SummaryText = string.Format(CultureInfo.CurrentCulture, UiText.Workshop_ActionSummary, done, Items.Count);
        IsRunning = false;
        IsFinished = true;
    }

    private void OnEvent(WorkshopActionEvent report)
    {
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
