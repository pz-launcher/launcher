using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Mods.Modpacks;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// The launcher's local collections: kept, named and ordered here; the selected one's items listed with their state for the account Steam
/// signs in with and the install in use, like an opened Steam collection.
/// </summary>
public sealed partial class LocalCollectionsViewModel : WorkshopItemListSectionViewModel, IReorderableList
{
    private readonly IClipboardService _clipboard;
    private readonly ModpackStore _modpacks;

    // The selected collection; null shows the first one.
    private Guid? _selectedId;

    internal LocalCollectionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls,
        WorkshopFavorites favorites,
        WorkshopTitles titles,
        LocalCollections localCollections,
        ModpackStore modpacks)
        : base(page, service, runner, dialogs, urls, favorites, titles, localCollections)
    {
        _clipboard = clipboard;
        _modpacks = modpacks;
        Collections = [];
        AddStateCommands(MoveUpCommand, MoveDownCommand, MoveToTopCommand, MoveToBottomCommand);
        RefreshCollections();

        // Never unsubscribed: the sections and the local collections are app-lifetime singletons.
        localCollections.Changed += (_, _) => OnLocalCollectionsChanged();
    }

    /// <summary>Every local collection, in the user's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<LocalCollectionRow> Collections { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanReorder))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(AddItemsCommand))]
    public partial LocalCollectionRow? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    /// <summary>No local collection exists.</summary>
    public bool IsEmpty => Collections.Count == 0;

    [ObservableProperty] public partial bool IsAddingItems { get; private set; }

    /// <summary>Reordering is a local write, never Steam's: available while a game, a load or a Steam action runs.</summary>
    public bool CanReorder => HasSelection;

    protected override string GeneratedCollectionName => Selected?.Name ?? "";

    private LocalCollection? Current => Selected is { } row ? LocalCollections.Find(row.Id) : null;

    /// <summary>Lists the selected collection's items again, so ones added elsewhere since appear.</summary>
    internal override Task ShowAsync() => LoadItemsAsync();

    internal override Task RefreshAsync() => LoadItemsAsync();

    [RelayCommand]
    private Task SelectAsync(LocalCollectionRow row)
    {
        if (row.Id == Selected?.Id)
        {
            return Task.CompletedTask;
        }

        _selectedId = row.Id;
        ShownStatus.Clear();
        RefreshCollections();
        return LoadItemsAsync();
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (await LocalCollectionPrompts.AskNameAsync(Dialogs, LocalCollections, UiText.LocalCollections_NewTitle, "", UiText.ModCollections_Create) is not { } name)
        {
            return;
        }

        _selectedId = LocalCollections.Create(name, []).Id;
        ShownStatus.Clear();
        RefreshCollections();
        await LoadItemsAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        if (Current is not { } collection
            || await LocalCollectionPrompts.AskNameAsync(
                Dialogs, LocalCollections, UiText.LocalCollections_RenameTitle, collection.Name, UiText.ModCollections_Rename, collection.Id) is not { } name)
        {
            return;
        }

        LocalCollections.Rename(collection.Id, name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (Current is not { } collection)
        {
            return;
        }

        var users = _modpacks.UsersOfLocalCollection(collection.Id);
        var choice = await ConfirmDeleteAsync(collection, users);
        if (choice == ConfirmChoice.Cancel)
        {
            return;
        }

        if (choice == ConfirmChoice.Alternative)
        {
            foreach (var install in users.GroupBy(user => user.InstallRoot))
            {
                _modpacks.Delete(install.Key, [.. install.Select(user => user.Name)]);
            }
        }

        _selectedId = null;
        ShownStatus.Clear();

        // The modpacks still using it keep their copy: the store makes them independent of it.
        LocalCollections.Delete(collection.Id);
        await LoadItemsAsync();
    }

    /// <summary>Adds pasted Workshop links and ids at the end; ids already listed and ids of Steam collections are skipped and counted.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AddItemsAsync()
    {
        // Offered only when it holds an id, so unrelated clipboard text never shows up in the box.
        var clipboard = await _clipboard.GetTextAsync();
        var initial = clipboard is not null && WorkshopLinks.ParseLines(clipboard).Ids.Count > 0 ? clipboard.Trim() : "";
        var prompt = new TextPrompt(
            UiText.LocalCollections_AddItemsTitle,
            UiText.LocalCollections_AddItemsMessage,
            initial,
            UiText.LocalCollections_AddItems,
            text => WorkshopLinks.ParseLines(text).Ids.Count > 0 ? null : UiText.LocalCollections_AddItemsNothing,
            IsMultiline: true);
        if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted || Current is not { } collection)
        {
            return;
        }

        var pasted = WorkshopLinks.ParseLines(accepted.Text);
        List<ulong> fresh = [.. pasted.Ids.Where(id => !collection.Items.Any(item => item.Id == id))];
        var listed = pasted.Ids.Count - fresh.Count;
        List<WorkshopEntry> items = [];
        var collections = 0;
        if (fresh.Count > 0)
        {
            IsAddingItems = true;
            Result<IReadOnlyList<WorkshopIdKind>> kinds;
            try
            {
                // Nothing is guessed: without the Web API's answer a collection id could be added as an item.
                kinds = await Service.ClassifyAsync(fresh);
            }
            finally
            {
                IsAddingItems = false;
            }

            if (!kinds.IsSuccess)
            {
                await Dialogs.ShowErrorAsync(ErrorMessages.ToUserText(kinds.Error));
                return;
            }

            foreach (var kind in kinds.Value)
            {
                if (kind.IsCollection)
                {
                    collections++;
                }
                else
                {
                    items.Add(new WorkshopEntry(kind.Id, kind.Title));
                }
            }
        }

        var added = LocalCollections.Add(collection.Id, items);
        var skippedLines = pasted.SkippedLines;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_ItemsAdded, added, listed, collections, skippedLines));
        await LoadItemsAsync();
    }

    [RelayCommand]
    private void RemoveItem(WorkshopItemRow row)
    {
        if (Selected is { } selected)
        {
            LocalCollections.Remove(selected.Id, row.Id);
        }
    }

    // The parameter is null while a row's bindings are still being set up.
    private bool CanMoveEarlier(WorkshopItemRow row) => row is not null && IndexOf(row) > 0;

    private bool CanMoveLater(WorkshopItemRow row) => row is not null && IndexOf(row) is >= 0 and var index && index < Rows.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private void MoveUp(WorkshopItemRow row) => Move(IndexOf(row), IndexOf(row) - 1);

    [RelayCommand(CanExecute = nameof(CanMoveLater))]
    private void MoveDown(WorkshopItemRow row) => Move(IndexOf(row), IndexOf(row) + 1);

    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private void MoveToTop(WorkshopItemRow row) => Move(IndexOf(row), 0);

    [RelayCommand(CanExecute = nameof(CanMoveLater))]
    private void MoveToBottom(WorkshopItemRow row) => Move(IndexOf(row), Rows.Count - 1);

    /// <summary>Moves one item of the selected collection, by its place in the rows shown.</summary>
    public void Move(int from, int to)
    {
        if (Selected is not { } selected || from < 0 || to < 0 || from >= Rows.Count || to >= Rows.Count || Current is not { } collection)
        {
            return;
        }

        // The rows can lack items the subscriptions couldn't be read for, so the move goes by item ids.
        var item = Rows[from].Id;
        var target = Rows[to].Id;
        var ids = collection.Items.Select(entry => entry.Id).ToList();
        LocalCollections.Move(selected.Id, ids.IndexOf(item), ids.IndexOf(target));
    }

    protected override void OnFavoritesChanged() => ShowRowFavorites();

    protected override Task ShowAgainAsync() => LoadItemsAsync();

    /// <summary>Asks before deleting the collection; <see cref="ConfirmChoice.Alternative"/> only when modpacks use it.</summary>
    private async Task<ConfirmChoice> ConfirmDeleteAsync(LocalCollection collection, IReadOnlyList<ModpackUser> users)
    {
        if (users.Count > 0)
        {
            return await ModpackPrompts.ConfirmDeleteUsedAsync(
                Dialogs,
                UiText.LocalCollections_DeleteTitle,
                string.Format(CultureInfo.CurrentCulture, UiText.Modpacks_LocalCollectionUsedMessage, collection.Name),
                ModpackPrompts.UserNames(_modpacks, users, Page.Install?.RootPath));
        }

        var confirmed = await Dialogs.ConfirmAsync(new ConfirmPrompt(
            UiText.LocalCollections_DeleteTitle,
            string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_DeleteMessage, collection.Name),
            UiText.ModCollections_Delete,
            UiText.Dialog_Cancel));
        return confirmed ? ConfirmChoice.Confirm : ConfirmChoice.Cancel;
    }

    /// <summary>The selected collection's items: their latest versions from the Web API, merged with the subscriptions like an opened collection.</summary>
    private Task LoadItemsAsync() => RunLoadAsync(async version =>
    {
        if (Current is not { } collection)
        {
            SetItems(null);
            SetRows([]);
            return;
        }

        List<ulong> ids = [.. collection.Items.Select(entry => entry.Id)];
        Dictionary<ulong, WorkshopItemDetails> latest = [];
        Error? detailsError = null;
        if (ids.Count > 0)
        {
            var details = await Service.CheckUpdatesAsync(ids);
            if (!IsLatest(version))
            {
                return;
            }

            if (details.IsSuccess)
            {
                latest = details.Value.ToDictionary(item => item.Id);
            }
            else
            {
                detailsError = details.Error;
            }
        }

        // The list as it is now: it may have changed while the Web API answered.
        if (Current is not { } current)
        {
            SetItems(null);
            SetRows([]);
            return;
        }

        SetItems([.. current.Items.Select(entry => new WorkshopCollectionItem(entry.Id, latest.GetValueOrDefault(entry.Id)))]);
        await MergeAsync(version);
        if (IsLatest(version) && detailsError is not null)
        {
            AddWarning(detailsError);
        }
    });

    // Removals, moves, renames and titles show at once; added items wait for the next load, which reads their details.
    private void OnLocalCollectionsChanged()
    {
        RefreshCollections();
        if (Current is { } collection)
        {
            ArrangeItems([.. collection.Items.Select(item => item.Id)]);
        }
    }

    private void RefreshCollections()
    {
        var all = LocalCollections.All;
        var selected = all.FirstOrDefault(collection => collection.Id == _selectedId) ?? all.FirstOrDefault();
        _selectedId = selected?.Id;
        Collections = [.. all.Select(collection => new LocalCollectionRow(collection.Id, collection.Name, collection.Items.Count, collection.Id == _selectedId))];
        Selected = Collections.FirstOrDefault(row => row.IsSelected);
    }

    private int IndexOf(WorkshopItemRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Id == row.Id)
            {
                return i;
            }
        }

        return -1;
    }
}
