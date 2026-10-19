using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>What the Workshop sections share around a Steam action: the unsubscribe confirmation, the progress dialog and the rows' download state.</summary>
internal static class WorkshopActionFlow
{
    /// <summary>Shows the progress dialog for the run and mirrors download progress on the rows; after an update it re-reads which items are still outdated.</summary>
    /// <param name="rows">The rows shown when an event arrives; a reload may replace them during the run.</param>
    public static async Task RunAsync(
        IWorkshopActionRunner runner,
        IWorkshopService service,
        IDialogService dialogs,
        string root,
        string extraGameArgs,
        WorkshopAction action,
        IReadOnlyList<WorkshopActionItemRow> items,
        Func<IReadOnlyList<WorkshopItemRow>> rows)
    {
        try
        {
            var dialog = new WorkshopActionViewModel(
                runner,
                root,
                action,
                items,
                report => ShowEvent(rows(), report),
                action == WorkshopAction.Update ? ct => StillOutdatedAsync(service, root, extraGameArgs, ct) : null);
            await dialogs.ShowWorkshopActionAsync(dialog);
        }
        finally
        {
            foreach (var row in rows())
            {
                row.ClearDownload();
            }
        }
    }

    /// <summary>Names the launcher mod collections that use the items' mods.</summary>
    public static Task<bool> ConfirmUnsubscribeAsync(IDialogService dialogs, IReadOnlyList<WorkshopItemRow> rows)
    {
        List<string> collections =
        [
            .. rows.SelectMany(row => row.Item.Collections).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase),
        ];
        var message = collections.Count == 0
            ? string.Format(CultureInfo.CurrentCulture, UiText.Workshop_UnsubscribeMessage, rows.Count)
            : string.Format(CultureInfo.CurrentCulture, UiText.Workshop_UnsubscribeUsedMessage, rows.Count, string.Join(", ", collections));
        return dialogs.ConfirmAsync(
            new ConfirmPrompt(UiText.Workshop_UnsubscribeTitle, message, UiText.Workshop_Unsubscribe, UiText.Dialog_Cancel)
            {
                Items = [.. rows.Select(row => NameWithId(row.Item))],
            });
    }

    // Not a Steam action, so callers keep it available while the game runs.
    public static async Task OpenInSteamAsync(IUrlLauncher urls, IDialogService dialogs, ulong id)
    {
        if (!await urls.OpenAsync(WorkshopLinks.ClientPage(id)))
        {
            await dialogs.ShowErrorAsync(UiText.Workshop_OpenFailed);
        }
    }

    public static List<WorkshopActionItemRow> ActionItems(IEnumerable<WorkshopItemRow> rows) => ActionItems(rows.Select(row => row.Item));

    public static List<WorkshopActionItemRow> ActionItems(IEnumerable<WorkshopItem> items) =>
        [.. items.Select(item => new WorkshopActionItemRow(item.Id, WorkshopItemRow.TitleOf(item)))];

    public static string NameWithId(WorkshopItem item)
    {
        var id = item.Id.ToString(CultureInfo.InvariantCulture);
        return WorkshopItemRow.WebTitle(item) is { } title ? string.Format(CultureInfo.CurrentCulture, UiText.Mods_NameWithId, title, id) : id;
    }

    private static void ShowEvent(IReadOnlyList<WorkshopItemRow> rows, WorkshopActionEvent report)
    {
        switch (report)
        {
            case WorkshopItemProgress progress:
                rows.FirstOrDefault(row => row.Id == progress.Id)?.ShowDownload(progress.Bytes, progress.Total);
                break;
            case WorkshopItemFinished finished:
                rows.FirstOrDefault(row => row.Id == finished.Outcome.Id)?.ClearDownload();
                break;
        }
    }

    // A read that fails verifies nothing; the dialog then trusts Steam's own report.
    private static async Task<IReadOnlySet<ulong>> StillOutdatedAsync(IWorkshopService service, string root, string extraGameArgs, CancellationToken ct)
    {
        var result = await service.GetSubscriptionsAsync(root, extraGameArgs, ct);
        return result.IsSuccess
            ? result.Value.Items.Where(item => item.State == WorkshopItemState.NeedsUpdate).Select(item => item.Id).ToHashSet()
            : new HashSet<ulong>();
    }
}
