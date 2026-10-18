using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The game's mod collections of the install in use: edit, order, share, import, and use one as the current collection.</summary>
public partial class ModCollectionsViewModel : ViewModelBase
{
    private const int MaxAddCandidates = 50;

    private readonly ModsViewModel _page;
    private readonly IModCollectionDefaultsService _defaults;
    private readonly ModCollectionApplier _applier;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;

    // The selected collection's ids in the shown order, ahead of the file while a reorder waits.
    private List<string> _ids = [];
    private string? _selectedName;
    private readonly LocalizedText _status;

    internal ModCollectionsViewModel(
        ModsViewModel page,
        IModCollectionDefaultsService defaults,
        ModCollectionApplier applier,
        IDialogService dialogs,
        IClipboardService clipboard)
    {
        _page = page;
        _defaults = defaults;
        _applier = applier;
        _dialogs = dialogs;
        _clipboard = clipboard;
        Rows = [];
        AddCandidates = [];
        _status = new LocalizedText(text => StatusText = text);
        page.PropertyChanged += OnPagePropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ModCollectionRow> Rows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(UseText), nameof(UseHint))]
    [NotifyCanExecuteChangedFor(
        nameof(RenameCommand), nameof(DeleteCommand), nameof(UseCommand), nameof(AutoSortCommand),
        nameof(ShareCommand), nameof(AddModCommand))]
    public partial ModCollectionRow? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    public string UseText => Selected switch
    {
        { IsCurrent: true, IsActiveInOrder: true } => UiText.ModCollections_InUse,
        { IsCurrent: true } => UiText.ModCollections_ApplyAgain,
        _ => UiText.ModCollections_Use,
    };

    public string UseHint => Selected switch
    {
        { IsCurrent: true, IsActiveInOrder: true } => UiText.ModCollections_InUseHint,
        { IsCurrent: true } => UiText.ModCollections_ApplyAgainHint,
        _ => UiText.ModCollections_UseHint,
    };

    /// <summary>Loaded, and the cache folder holds no collection.</summary>
    public bool IsEmpty => _page.State is not null && Rows.Count == 0;

    public ObservableCollection<ModCollectionEntryRow> Entries { get; } = [];

    [ObservableProperty] public partial string AddSearchText { get; set; } = "";

    /// <summary>Installed mods not in the selected collection matching <see cref="AddSearchText"/>; none for an empty search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddCandidates))]
    public partial IReadOnlyList<InstalledMod> AddCandidates { get; private set; }

    public bool HasAddCandidates => AddCandidates.Count > 0;

    [ObservableProperty] public partial string? StatusText { get; private set; }

    [RelayCommand]
    private void Select(ModCollectionRow row)
    {
        if (row.Name == _selectedName)
        {
            return;
        }

        _selectedName = row.Name;
        AddSearchText = "";
        _status.Clear();
        Rebuild(_page.State);
    }

    private bool CanWrite() => _page.CanWrite;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task NewAsync()
    {
        var prompt = ModCollectionPrompts.NamePrompt(UiText.ModCollections_NewTitle, "", UiText.ModCollections_Create);
        if (await AskNameAsync(prompt, original: null) is { } name)
        {
            await SaveAndSelectAsync(name, []);
        }
    }

    private bool CanCreateFromEnabled() => _page.CanWrite && _page.State is { ActiveIds.Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanCreateFromEnabled))]
    private async Task NewFromEnabledAsync()
    {
        if (_page.State is not { } state)
        {
            return;
        }

        var prompt = ModCollectionPrompts.NamePrompt(UiText.ModCollections_NewFromEnabledTitle, "", UiText.ModCollections_Create);
        if (await AskNameAsync(prompt, original: null) is { } name)
        {
            await SaveAndSelectAsync(name, state.ActiveIds);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task ImportAsync()
    {
        // Offered only when it holds a collection line, so unrelated clipboard text never shows up in the box.
        var clipboard = await _clipboard.GetTextAsync();
        var initial = clipboard is not null && ModCollectionShareText.Parse(clipboard).Count > 0 ? clipboard.Trim() : "";
        var prompt = new TextPrompt(
            UiText.ModCollections_ImportTitle, UiText.ModCollections_ImportMessage, initial, UiText.ModCollections_Import, ValidateImport, IsMultiline: true);
        if (await _dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted)
        {
            return;
        }

        var imported = new List<SharedModCollection>();
        foreach (var collection in ModCollectionShareText.Parse(accepted.Text))
        {
            if (!Exists(collection.Name) || await ModCollectionPrompts.ConfirmOverwriteAsync(_dialogs, collection.Name))
            {
                imported.Add(collection);
            }
        }

        if (imported.Count == 0)
        {
            return;
        }

        if (await _page.WriteAsync(async (root, extraGameArgs) =>
            {
                foreach (var collection in imported)
                {
                    var saved = await _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, null, collection.Name, collection.ModIds));
                    if (!saved.IsSuccess)
                    {
                        return saved;
                    }
                }

                return Result.Ok();
            }))
        {
            SelectByName(imported[^1].Name);
        }
    }

    private bool CanWriteSelected() => _page.CanWrite && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private async Task RenameAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var original = row.Name;
        var prompt = ModCollectionPrompts.NamePrompt(UiText.ModCollections_RenameTitle, original, UiText.ModCollections_Rename);
        if (await AskNameAsync(prompt, original) is not { } name || name == original)
        {
            return;
        }

        IReadOnlyList<string> ids = [.. _ids];
        if (!await _page.WriteAsync((root, extraGameArgs) =>
                _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, original, name, ids))))
        {
            return;
        }

        if (_page.Install is { } install && _defaults.Get(install.RootPath) == original)
        {
            _defaults.Save(install.RootPath, name);
        }

        SelectByName(name);
    }

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private async Task DeleteAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = row.Name;
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmPrompt(
            UiText.ModCollections_DeleteTitle,
            Format(UiText.ModCollections_DeleteMessage, name),
            UiText.ModCollections_Delete,
            UiText.Dialog_Cancel));
        if (!confirmed || !await _page.WriteAsync((root, extraGameArgs) => _page.Service.DeleteAsync(root, extraGameArgs, name)))
        {
            return;
        }

        if (_page.Install is { } install && _defaults.Get(install.RootPath) == name)
        {
            _defaults.Clear(install.RootPath);
        }

        SelectByName(null);
    }

    private bool CanUse() => CanWriteSelected() && Selected is not { IsCurrent: true, IsActiveInOrder: true };

    [RelayCommand(CanExecute = nameof(CanUse))]
    private async Task UseAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = row.Name;
        ModCollectionApplyOutcome? outcome = null;
        await _page.RunAsync(async (root, extraGameArgs) =>
        {
            outcome = await _applier.ApplyAsync(root, extraGameArgs, name);
            if (outcome is ModCollectionApplyOutcome.Applied && _defaults.Get(root) != name)
            {
                _defaults.Save(root, name);
            }
        });
        switch (outcome)
        {
            case ModCollectionApplyOutcome.Applied applied:
                var count = applied.Result.EnabledIds.Count;
                _status.Show(() => Format(UiText.ModCollections_Applied, name, count));
                break;
            case ModCollectionApplyOutcome.Failed failed:
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(failed.Error));
                break;
            case ModCollectionApplyOutcome.Cancelled or null:
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private async Task AutoSortAsync()
    {
        if (_page.State is not { } state)
        {
            return;
        }

        var sorted = ModLoadOrder.AutoSort(_ids, state.InstalledMods);
        if (!sorted.SequenceEqual(_ids, StringComparer.Ordinal))
        {
            await SaveIdsAsync(sorted);
        }
    }

    private bool HasSelectionCommand() => Selected is not null;

    // Not a file write, so it stays available while the game runs.
    [RelayCommand(CanExecute = nameof(HasSelectionCommand))]
    private async Task ShareAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        await _clipboard.SetTextAsync(ModCollectionShareText.Format(row.Name, _ids));
        var name = row.Name;
        _status.Show(() => Format(UiText.ModCollections_Shared, name));
    }

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private Task AddModAsync(InstalledMod mod) =>
        _ids.Contains(mod.Id, StringComparer.Ordinal) ? Task.CompletedTask : SaveIdsAsync([.. _ids, mod.Id]);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task RemoveModAsync(ModCollectionEntryRow entry) =>
        SaveIdsAsync([.. _ids.Where(id => id != entry.Id)]);

    /// <summary>Rows may be dragged to a new place.</summary>
    public bool CanReorder => _page.CanWrite;

    // The parameter is null while a row's bindings are still being set up.
    private bool CanMoveEarlier(ModCollectionEntryRow entry) => _page.CanWrite && entry is not null && entry.Number > 1;

    private bool CanMoveLater(ModCollectionEntryRow entry) => _page.CanWrite && entry is not null && entry.Number < Entries.Count;

    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private void MoveUp(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), Entries.IndexOf(entry) - 1);

    [RelayCommand(CanExecute = nameof(CanMoveLater))]
    private void MoveDown(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), Entries.IndexOf(entry) + 1);

    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private void MoveToTop(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), 0);

    [RelayCommand(CanExecute = nameof(CanMoveLater))]
    private void MoveToBottom(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), Entries.Count - 1);

    /// <summary>Moves one mod of the selected collection; the new order is written after a short pause, so quick moves write once.</summary>
    public void Move(int from, int to)
    {
        if (!_page.CanEdit || Selected is not { } row || from == to || from < 0 || to < 0 || from >= _ids.Count || to >= _ids.Count)
        {
            return;
        }

        var id = _ids[from];
        _ids.RemoveAt(from);
        _ids.Insert(to, id);
        Entries.Move(from, to);
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            Entries[i].Number = i + 1;
        }

        NotifyMoveCommands();
        _page.ScheduleOrderSave(row.Name, [.. _ids]);
        RefreshSelectedInUse(row);
    }

    partial void OnAddSearchTextChanged(string value) => RefreshCandidates();

    internal void RenderTexts()
    {
        _status.Render();
        OnPropertyChanged(nameof(UseText));
        OnPropertyChanged(nameof(UseHint));
    }

    private async Task SaveIdsAsync(IReadOnlyList<string> ids)
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = row.Name;
        await _page.WriteAsync((root, extraGameArgs) => _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, name, name, ids)));
    }

    private async Task SaveAndSelectAsync(string name, IReadOnlyList<string> ids)
    {
        if (await _page.WriteAsync((root, extraGameArgs) =>
                _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, null, name, ids))))
        {
            SelectByName(name);
        }
    }

    /// <summary>The trimmed name, or <c>null</c> when cancelled.</summary>
    /// <param name="original">The collection being renamed, which its own name doesn't clash with.</param>
    private async Task<string?> AskNameAsync(TextPrompt prompt, string? original) =>
        await ModCollectionPrompts.AskNameAsync(_dialogs, prompt, name => name != original && Exists(name)) is TextPromptResult.Accepted accepted
            ? accepted.Text
            : null;

    private static string? ValidateImport(string text)
    {
        var collections = ModCollectionShareText.Parse(text);
        if (collections.Count == 0)
        {
            return UiText.ModCollections_ImportNothing;
        }

        return collections.FirstOrDefault(collection => !ModCollectionName.IsValid(collection.Name)) is { } invalid
            ? Format(UiText.ModCollections_ImportNameInvalid, invalid.Name)
            : null;
    }

    internal bool Exists(string name) => _page.State?.Collections.Any(collection => collection.Name == name) ?? false;

    internal void SelectByName(string? name)
    {
        _selectedName = name;
        Rebuild(_page.State);
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ModsViewModel.State):
                Rebuild(_page.State);
                break;
            case nameof(ModsViewModel.CanWrite):
                NotifyWriteCommands();
                OnPropertyChanged(nameof(CanReorder));
                break;
            case nameof(ModsViewModel.Install):
                _status.Clear();
                break;
        }
    }

    private void NotifyWriteCommands()
    {
        NewCommand.NotifyCanExecuteChanged();
        NewFromEnabledCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        UseCommand.NotifyCanExecuteChanged();
        AutoSortCommand.NotifyCanExecuteChanged();
        AddModCommand.NotifyCanExecuteChanged();
        RemoveModCommand.NotifyCanExecuteChanged();
        NotifyMoveCommands();
    }

    private void NotifyMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveToTopCommand.NotifyCanExecuteChanged();
        MoveToBottomCommand.NotifyCanExecuteChanged();
    }

    private void Rebuild(ModCollectionState? state)
    {
        if (state is null)
        {
            Rows = [];
            Selected = null;
            _ids = [];
            Entries.Clear();
            RefreshCandidates();
            NotifyWriteCommands();
            return;
        }

        var installed = state.InstalledMods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var currentName = _page.Install is { } install ? _defaults.Get(install.RootPath) : null;
        var selected = state.Collections.FirstOrDefault(collection => collection.Name == _selectedName) ?? state.Collections.FirstOrDefault();
        _selectedName = selected?.Name;
        Rows =
        [
            .. state.Collections.Select(collection =>
            {
                // A queued reorder isn't in the files yet, so Core's in-use flag still describes the old order.
                var shown = _page.PendingOrderOf(collection.Name) is { } pending
                    ? ModCollection.Create(collection.Name, pending, state.InstalledMods, state.ActiveIds)
                    : collection;
                return new ModCollectionRow(
                    collection.Name,
                    collection.ModIds.Count,
                    collection.ModIds.Count(id => !installed.Contains(id)),
                    collection.Name == currentName,
                    shown.IsActiveInOrder,
                    collection.Name == _selectedName);
            }),
        ];
        Selected = Rows.FirstOrDefault(row => row.IsSelected);

        _ids = selected is null ? [] : [.. _page.PendingOrderOf(selected.Name) ?? selected.ModIds];
        var names = state.InstalledMods.ToDictionary(mod => mod.Id, mod => mod.Name, StringComparer.Ordinal);
        Entries.Clear();
        for (var i = 0; i < _ids.Count; i++)
        {
            var id = _ids[i];
            Entries.Add(names.TryGetValue(id, out var name) ? new ModCollectionEntryRow(id, name, isMissing: false, i + 1) : new ModCollectionEntryRow(id, id, isMissing: true, i + 1));
        }

        RefreshCandidates();
        NotifyWriteCommands();
    }

    private void RefreshSelectedInUse(ModCollectionRow row)
    {
        if (_page.State is not { } state)
        {
            return;
        }

        var shown = ModCollection.Create(row.Name, _ids, state.InstalledMods, state.ActiveIds);
        var updated = row with { IsActiveInOrder = shown.IsActiveInOrder };
        if (updated != row)
        {
            Rows = [.. Rows.Select(other => other.Name == row.Name ? updated : other)];
            Selected = updated;
        }
    }

    private void RefreshCandidates()
    {
        var search = AddSearchText.Trim();
        if (search.Length == 0 || _page.State is not { } state || Selected is null)
        {
            AddCandidates = [];
            return;
        }

        var inCollection = _ids.ToHashSet(StringComparer.Ordinal);
        AddCandidates =
        [
            .. state.InstalledMods
                .Where(mod => !inCollection.Contains(mod.Id) && InstalledModsViewModel.Matches(mod, search))
                .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxAddCandidates),
        ];
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
