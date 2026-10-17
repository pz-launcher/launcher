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
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The game's mod collections of the install in use: edit, order, share, import, apply and mark the default.</summary>
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
    private Func<string>? _statusSource;

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
        page.PropertyChanged += OnPagePropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ModCollectionRow> Rows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(
        nameof(RenameCommand), nameof(DeleteCommand), nameof(ApplyCommand), nameof(AutoSortCommand),
        nameof(ShareCommand), nameof(ToggleDefaultCommand), nameof(AddModCommand))]
    public partial ModCollectionRow? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

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
        ShowStatus(null);
        Rebuild(_page.State);
    }

    private bool CanWrite() => _page.CanWrite;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task NewAsync()
    {
        var prompt = NamePrompt(UiText.ModCollections_NewTitle, "", UiText.ModCollections_Create);
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

        var prompt = NamePrompt(UiText.ModCollections_NewFromEnabledTitle, "", UiText.ModCollections_Create);
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
        var prompt = NamePrompt(UiText.ModCollections_RenameTitle, original, UiText.ModCollections_Rename);
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

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private async Task ApplyAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = row.Name;
        ModCollectionApplyOutcome? outcome = null;
        await _page.RunAsync(async (root, extraGameArgs) => outcome = await _applier.ApplyAsync(root, extraGameArgs, name));
        switch (outcome)
        {
            case ModCollectionApplyOutcome.Applied applied:
                var count = applied.Result.EnabledIds.Count;
                ShowStatus(() => Format(UiText.ModCollections_Applied, name, count));
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
        ShowStatus(() => Format(UiText.ModCollections_Shared, name));
    }

    private bool CanToggleDefault() => Selected is not null && _page.Install is not null;

    [RelayCommand(CanExecute = nameof(CanToggleDefault))]
    private void ToggleDefault()
    {
        if (Selected is not { } row || _page.Install is not { } install)
        {
            return;
        }

        if (row.IsDefault)
        {
            _defaults.Clear(install.RootPath);
        }
        else
        {
            _defaults.Save(install.RootPath, row.Name);
        }

        Rebuild(_page.State);
    }

    [RelayCommand(CanExecute = nameof(CanWriteSelected))]
    private Task AddModAsync(InstalledMod mod) =>
        _ids.Contains(mod.Id, StringComparer.Ordinal) ? Task.CompletedTask : SaveIdsAsync([.. _ids, mod.Id]);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task RemoveModAsync(ModCollectionEntryRow entry) =>
        SaveIdsAsync([.. _ids.Where(id => id != entry.Id)]);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void MoveUp(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), Entries.IndexOf(entry) - 1);

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void MoveDown(ModCollectionEntryRow entry) => Move(Entries.IndexOf(entry), Entries.IndexOf(entry) + 1);

    /// <summary>Moves one mod of the selected collection; the new order is written after a short pause, so a drag writes once.</summary>
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

        _page.ScheduleOrderSave(row.Name, [.. _ids]);
    }

    partial void OnAddSearchTextChanged(string value) => RefreshCandidates();

    internal void RenderTexts() => StatusText = _statusSource?.Invoke();

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

    private static TextPrompt NamePrompt(string title, string initial, string accept) =>
        new(title, UiText.ModCollections_NameMessage, initial, accept, ModCollectionPrompts.ValidateName);

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

    private bool Exists(string name) => _page.State?.Collections.Any(collection => collection.Name == name) ?? false;

    private void SelectByName(string? name)
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
                break;
            case nameof(ModsViewModel.Install):
                ShowStatus(null);
                ToggleDefaultCommand.NotifyCanExecuteChanged();
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
        ApplyCommand.NotifyCanExecuteChanged();
        AutoSortCommand.NotifyCanExecuteChanged();
        AddModCommand.NotifyCanExecuteChanged();
        RemoveModCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
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
        var defaultName = _page.Install is { } install ? _defaults.Get(install.RootPath) : null;
        var selected = state.Collections.FirstOrDefault(collection => collection.Name == _selectedName) ?? state.Collections.FirstOrDefault();
        _selectedName = selected?.Name;
        Rows =
        [
            .. state.Collections.Select(collection => new ModCollectionRow(
                collection.Name,
                collection.ModIds.Count,
                collection.ModIds.Count(id => !installed.Contains(id)),
                collection.Name == defaultName,
                collection.IsActive,
                collection.Name == _selectedName)),
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

    private void ShowStatus(Func<string>? source)
    {
        _statusSource = source;
        StatusText = source?.Invoke();
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
