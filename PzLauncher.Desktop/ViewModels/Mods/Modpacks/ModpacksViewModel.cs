using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.ViewModels.Mods.Collections;

namespace PzLauncher.Desktop.ViewModels.Mods.Modpacks;

/// <summary>
/// The install's modpacks: a mod preset paired with a Steam Workshop or local collection. The selected one's collection items, then the
/// Workshop items of its preset's mods that the collection lacks, are listed with their state like an opened collection.
/// </summary>
public sealed partial class ModpacksViewModel : WorkshopItemListSectionViewModel
{
    private readonly IClipboardService _clipboard;
    private readonly ModpackStore _modpacks;
    private readonly WorkshopCollectionHistory _history;
    private readonly WorkshopUserCollectionsCache _userCollections;

    // Steam collections read this session; a refresh reads the shown one again.
    private readonly Dictionary<ulong, WorkshopCollection> _steamCollections = [];

    // The selected modpack; null shows the first one.
    private string? _selectedName;

    // The modpack whose items are listed, so a change that lists other items lists them again.
    private Modpack? _listed;

    private Task _load = Task.CompletedTask;

    internal ModpacksViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls,
        WorkshopFavorites favorites,
        WorkshopTitles titles,
        LocalCollections localCollections,
        ModpackStore modpacks,
        WorkshopCollectionHistory history,
        WorkshopUserCollectionsCache userCollections)
        : base(page, service, runner, dialogs, urls, favorites, titles, localCollections)
    {
        _clipboard = clipboard;
        _modpacks = modpacks;
        _history = history;
        _userCollections = userCollections;
        Modpacks = [];
        AddStateCommands(NewCommand, EditCommand, UsePresetCommand, ImportCommand, ShareCommand);
        RefreshList();

        // Never unsubscribed: the sections, the page and the store are app-lifetime singletons.
        modpacks.Changed += (_, _) => _ = ShowSelectedAsync();
        page.PropertyChanged += OnPageStateChanged;
    }

    /// <summary>The install's modpacks, in the user's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ModpackRow> Modpacks { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RenameCommand), nameof(DeleteCommand), nameof(UsePresetCommand), nameof(ShareCommand))]
    public partial ModpackRow? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    /// <summary>The install has no modpack.</summary>
    public bool IsEmpty => Modpacks.Count == 0;

    protected override string GeneratedCollectionName => Selected?.Name ?? "";

    private Modpack? Current => _selectedName is { } name && Page.Install is { } install ? _modpacks.Find(install.RootPath, name) : null;

    /// <summary>Lists the selected modpack's items again, so their states and the install's mods are current.</summary>
    internal override Task ShowAsync()
    {
        RefreshList();
        return _load = LoadItemsAsync();
    }

    /// <summary>Reads the install's presets and the shown Steam collection again.</summary>
    internal override async Task RefreshAsync()
    {
        if (Current?.Collection is ModpackCollection.Steam steam)
        {
            _steamCollections.Remove(steam.Id);
        }

        await Page.ReloadAsync();
        await ShowAsync();
    }

    [RelayCommand]
    private Task SelectAsync(ModpackRow row)
    {
        if (row.Name == Selected?.Name)
        {
            return Task.CompletedTask;
        }

        _selectedName = row.Name;
        ShownStatus.Clear();
        RefreshList();
        return _load = LoadItemsAsync();
    }

    // A preset is picked from the install's, so they must have been read.
    private bool CanEdit() => Page.Install is not null && Page.State is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task NewAsync() => ShowEditAsync(null);

    private bool CanEditSelected() => CanEdit() && HasSelection;

    /// <summary>Picks another preset or collection; for an independent modpack, linking it again.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private Task EditAsync() => Current is { } modpack ? ShowEditAsync(modpack) : Task.CompletedTask;

    // A launcher write, never the game's or Steam's: available while a game, a load or a Steam action runs.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        if (Page.Install is not { } install || Current is not { } modpack)
        {
            return;
        }

        var root = install.RootPath;
        var prompt = new TextPrompt(
            UiText.Modpacks_RenameTitle,
            UiText.Modpacks_NameMessage,
            modpack.Name,
            UiText.ModCollections_Rename,
            text => ModpackPrompts.ValidateName(_modpacks, root, text, modpack.Name));
        if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted || Page.Install?.RootPath != root)
        {
            return;
        }

        var name = accepted.Text.Trim();
        if (name == modpack.Name)
        {
            return;
        }

        _selectedName = name;
        _modpacks.Rename(root, modpack.Name, name);
        await ShowSelectedAsync();
    }

    // A launcher write, never the game's or Steam's: available while a game, a load or a Steam action runs.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (Page.Install is not { } install || Current is not { } modpack)
        {
            return;
        }

        var root = install.RootPath;
        if (!await Dialogs.ConfirmAsync(new ConfirmPrompt(
                UiText.Modpacks_DeleteTitle,
                string.Format(CultureInfo.CurrentCulture, UiText.Modpacks_DeleteMessage, modpack.Name),
                UiText.ModCollections_Delete,
                UiText.Dialog_Cancel))
            || Page.Install?.RootPath != root)
        {
            return;
        }

        _selectedName = null;
        ShownStatus.Clear();
        _modpacks.Delete(root, [modpack.Name]);
        await ShowSelectedAsync();
    }

    private bool CanUsePreset() => Page.CanWrite && HasSelection;

    /// <summary>Makes the modpack's preset the install's current one; an independent copy is first written to the game's presets and linked.</summary>
    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private async Task UsePresetAsync()
    {
        if (Current is not { } modpack
            || (modpack.Preset.IsLinked ? modpack.Preset.Name : await RestorePresetAsync(modpack)) is not { } name)
        {
            return;
        }

        if (await Page.ApplyCollectionAsync(name) is ModCollectionApplyOutcome.Applied applied)
        {
            var count = applied.Result.EnabledIds.Count;
            ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.ModCollections_Applied, name, count));
        }
    }

    private bool CanShare() => HasSelection && Page.State is not null;

    // Not a file write, so it stays available while the game runs; the install's mods decide the extra items.
    [RelayCommand(CanExecute = nameof(CanShare))]
    private async Task ShareAsync()
    {
        if (Page.Install is not { } install || Current is not { } modpack)
        {
            return;
        }

        SharedModpackCollection collection;
        IReadOnlyCollection<ulong> collectionIds;
        switch (modpack.Collection)
        {
            case ModpackCollection.Steam steam:
                var read = await ReadSteamCollectionAsync(steam.Id);
                collection = new SharedModpackCollection.Steam(steam.Id);
                collectionIds = read.IsSuccess ? read.Value.Items.Select(item => item.Id).ToHashSet() : [];
                break;
            case ModpackCollection.Local local:
                List<ulong> items = [.. local.Items.Select(item => item.Id)];
                collection = new SharedModpackCollection.Local(local.Name, items);
                collectionIds = items;
                break;
            default:
                throw new InvalidOperationException($"Unknown modpack collection {modpack.Collection}.");
        }

        if (Page.State is not { } state)
        {
            return;
        }

        var extras = ModpackExtraItems.Compute(modpack.ExtraItems, modpack.Preset.ModIds, state.InstalledMods, collectionIds);
        if (!extras.SequenceEqual(modpack.ExtraItems))
        {
            _modpacks.SetExtraItems(install.RootPath, modpack.Name, extras);
        }

        var preset = modpack.Preset;
        await _clipboard.SetTextAsync(ModpackShareText.Format(
            new SharedModpack(modpack.Name, collection, extras, new SharedModCollection(preset.Name, preset.ModIds))));

        // The copy the game loads of each mod, as the extra items take it.
        var loaded = state.InstalledMods.DistinctBy(mod => mod.Id, StringComparer.Ordinal).ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        var notInstalled = preset.ModIds.Count(id => !loaded.ContainsKey(id));
        var notWorkshop = preset.ModIds.Count(id => loaded.GetValueOrDefault(id) is { WorkshopId: null });
        var name = modpack.Name;
        if (notInstalled + notWorkshop == 0)
        {
            ShownStatus.Show(() => Format(UiText.Modpacks_Shared, name));
        }
        else
        {
            ShownStatus.Show(() => Format(UiText.Modpacks_SharedPartly, name, notWorkshop, notInstalled));
        }
    }

    private bool CanImport() => Page.CanWrite;

    /// <summary>
    /// Writes the pasted modpack's preset, keeps its collection and saves it linked to both, then subscribes to its missing items in one Steam
    /// action. Every name clash is settled first, so a cancel writes nothing; a failed subscribe keeps what was imported.
    /// </summary>
    // The preset is written to the game's files, so the game must not run.
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (Page.Install is not { } install)
        {
            return;
        }

        var root = install.RootPath;

        // Offered only when it is modpack text, so unrelated clipboard text never shows up in the box.
        var clipboard = await _clipboard.GetTextAsync();
        var initial = clipboard is not null && ModpackShareText.Parse(clipboard) is not ModpackShareTextResult.NotModpack ? clipboard.Trim() : "";
        var prompt = new TextPrompt(
            UiText.Modpacks_ImportTitle, UiText.Modpacks_ImportMessage, initial, UiText.Modpacks_ImportContinue, ValidateImport, IsMultiline: true);
        if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted
            || ModpackShareText.Parse(accepted.Text) is not ModpackShareTextResult.Read read
            || !await ConfirmImportAsync(read)
            || await ResolveImportNamesAsync(root, read.Modpack) is not { } names
            || Page.Install?.RootPath != root)
        {
            return;
        }

        var shared = read.Modpack;
        if (!await Page.WriteAsync((writeRoot, extraGameArgs) =>
                Page.Service.SaveAsync(new SaveModCollectionRequest(writeRoot, extraGameArgs, null, names.Preset, shared.Preset.ModIds))))
        {
            return;
        }

        ModpackCollection collection;
        switch (shared.Collection)
        {
            case SharedModpackCollection.Steam steam:
                _history.Remember(steam.Id, SteamTitle(steam.Id));
                collection = new ModpackCollection.Steam(steam.Id);
                break;
            case SharedModpackCollection.Local local:
                var target = names.Local ?? new ImportedLocalName(local.Name, null);
                List<WorkshopEntry> items = [.. local.Items.Select(id => new WorkshopEntry(id, null))];
                var kept = (target.Replaces is { } replaced ? LocalCollections.Replace(replaced, items) : null) ?? LocalCollections.Create(target.Name, items);
                collection = new ModpackCollection.Local(kept.Id, kept.Name, kept.Items);
                break;
            default:
                throw new InvalidOperationException($"Unknown shared collection {shared.Collection}.");
        }

        var modpack = new Modpack(names.Modpack, new ModpackPreset(names.Preset, shared.Preset.ModIds, IsLinked: true), collection, shared.ExtraItems);
        _selectedName = modpack.Name;
        ShownStatus.Clear();
        _modpacks.Save(root, modpack, names.ReplacesModpack ? modpack.Name : null);
        await SettledLoadAsync();

        var name = modpack.Name;
        ShownStatus.Show(() => Format(UiText.Modpacks_Imported, name));
        if (Current?.Name == name)
        {
            await RunAsync(WorkshopAction.Subscribe, Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed));
        }
    }

    private static string? ValidateImport(string text)
    {
        switch (ModpackShareText.Parse(text))
        {
            case ModpackShareTextResult.Read { Modpack: var shared }:
                return !ModCollectionName.IsValid(shared.Preset.Name) ? Format(UiText.ModCollections_ImportNameInvalid, shared.Preset.Name)
                    : !ModpackName.IsValid(shared.Name) || shared.Collection is SharedModpackCollection.Local local && !LocalCollectionName.IsValid(local.Name)
                        ? UiText.Modpacks_ImportIncomplete
                        : null;
            case ModpackShareTextResult.NewerVersion:
                return UiText.Modpacks_ImportNewer;
            case ModpackShareTextResult.Incomplete:
                return UiText.Modpacks_ImportIncomplete;
            default:
                return UiText.Modpacks_ImportNothing;
        }
    }

    private Task<bool> ConfirmImportAsync(ModpackShareTextResult.Read read)
    {
        var shared = read.Modpack;
        var collection = shared.Collection switch
        {
            SharedModpackCollection.Steam steam => Format(UiText.Modpacks_ImportSteamCollection, IdText(steam.Id)),
            SharedModpackCollection.Local local => Format(UiText.Modpacks_ImportLocalCollection, local.Name, local.Items.Count),
            _ => throw new ArgumentException($"Unknown shared collection {shared.Collection}.", nameof(read)),
        };
        List<string> items =
        [
            collection,
            Format(UiText.Modpacks_ImportExtraItems, shared.ExtraItems.Count),
            Format(UiText.Modpacks_ImportPreset, shared.Preset.Name, shared.Preset.ModIds.Count),
        ];
        if (read.SkippedLines > 0)
        {
            items.Add(Format(UiText.Modpacks_ImportSkipped, read.SkippedLines));
        }

        return Dialogs.ConfirmAsync(new ConfirmPrompt(
            UiText.Modpacks_ImportTitle, Format(UiText.Modpacks_ImportConfirmMessage, shared.Name), UiText.Modpacks_SubscribeAndImport, UiText.Dialog_Cancel)
        {
            Items = items,
        });
    }

    /// <summary>Settles each name clash of the import before anything is written; <c>null</c> when one was cancelled.</summary>
    private async Task<ImportNames?> ResolveImportNamesAsync(string root, SharedModpack shared)
    {
        var modpack = shared.Name.Trim();
        var replacesModpack = false;
        if (_modpacks.IsNameTaken(root, modpack))
        {
            switch (await AskOverwriteAsync(UiText.Modpacks_OverwriteTitle, UiText.Modpacks_OverwriteMessage, modpack))
            {
                case ConfirmChoice.Confirm:
                    replacesModpack = true;
                    break;
                case ConfirmChoice.Alternative:
                    var prompt = new TextPrompt(
                        UiText.Modpacks_ImportTitle,
                        UiText.Modpacks_NameMessage,
                        modpack,
                        UiText.Modpacks_ImportContinue,
                        text => ModpackPrompts.ValidateName(_modpacks, root, text));
                    if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted)
                    {
                        return null;
                    }

                    modpack = accepted.Text.Trim();
                    break;
                case ConfirmChoice.Cancel:
                    return null;
            }
        }

        if (await ResolvePresetNameAsync(shared.Preset.Name, UiText.Modpacks_ImportTitle, UiText.Modpacks_ImportContinue) is not { } preset)
        {
            return null;
        }

        if (shared.Collection is not SharedModpackCollection.Local local)
        {
            return new ImportNames(modpack, replacesModpack, preset, null);
        }

        var localName = local.Name.Trim();
        if (LocalCollections.All.FirstOrDefault(collection => collection.Name == localName) is not { } existing)
        {
            return new ImportNames(modpack, replacesModpack, preset, new ImportedLocalName(localName, null));
        }

        var target = await AskOverwriteAsync(UiText.LocalCollections_OverwriteTitle, UiText.LocalCollections_OverwriteMessage, localName) switch
        {
            ConfirmChoice.Confirm => new ImportedLocalName(localName, existing.Id),
            ConfirmChoice.Alternative =>
                await LocalCollectionPrompts.AskNameAsync(Dialogs, LocalCollections, UiText.Modpacks_ImportTitle, localName, UiText.Modpacks_ImportContinue) is { } newName
                    ? new ImportedLocalName(newName, null)
                    : null,
            ConfirmChoice.Cancel => null,
        };
        return target is null ? null : new ImportNames(modpack, replacesModpack, preset, target);
    }

    /// <summary>Lets the loads the import started finish, each superseding the one before.</summary>
    private async Task SettledLoadAsync()
    {
        Task load;
        do
        {
            load = _load;
            await load;
        }
        while (load != _load);
    }

    protected override void OnFavoritesChanged()
    {
        ShowRowFavorites();
        RefreshList();
    }

    protected override Task ShowAgainAsync() => _load = LoadItemsAsync();

    protected override void ClearRows()
    {
        base.ClearRows();
        SetItems(null);
        _listed = null;
        _selectedName = null;
        RefreshList();
    }

    private static bool SameItems(Modpack? a, Modpack? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.ExtraItems.SequenceEqual(b.ExtraItems) && (a.Collection, b.Collection) switch
            {
                (ModpackCollection.Steam x, ModpackCollection.Steam y) => x.Id == y.Id,
                (ModpackCollection.Local x, ModpackCollection.Local y) => x.Items.Select(item => item.Id).SequenceEqual(y.Items.Select(item => item.Id)),
                _ => false,
            };

    private static string IdText(ulong id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private async Task ShowEditAsync(Modpack? original)
    {
        if (Page.Install is not { } install || Page.State is not { } state)
        {
            return;
        }

        var root = install.RootPath;
        var edit = new ModpackEditViewModel(
            original is null ? UiText.Modpacks_NewTitle : UiText.Modpacks_EditTitle,
            original,
            text => ModpackPrompts.ValidateName(_modpacks, root, text, original?.Name),
            state.Collections,
            await SteamOptionsAsync(),
            LocalCollections.All);
        if (!await Dialogs.EditModpackAsync(edit) || edit.Result is not { } result || Page.Install?.RootPath != root)
        {
            return;
        }

        var modpack = original is null
            ? new Modpack(result.Name, result.Preset, result.Collection, [])
            : original with { Name = result.Name, Preset = result.Preset, Collection = result.Collection };
        _selectedName = modpack.Name;
        ShownStatus.Clear();
        _modpacks.Save(root, modpack, original?.Name);
        await ShowSelectedAsync();
    }

    // Starred ones first, then the account's favorited and published ones Steam last listed, then the opened ones; each collection once.
    private async Task<IReadOnlyList<ModpackSteamOption>> SteamOptionsAsync()
    {
        var account = await Service.GetCurrentAccountAsync();
        var user = account.IsSuccess ? _userCollections.For(account.Value.AccountId)?.Collections : null;
        List<ModpackSteamOption> options = [];
        Add(Favorites.Collections, UiText.Mods_SectionWorkshopFavorites);
        Add(user?.Favorited.Select(summary => new WorkshopEntry(summary.Id, summary.Title)) ?? [], UiText.WorkshopCollections_Favorited);
        Add(user?.Published.Select(summary => new WorkshopEntry(summary.Id, summary.Title)) ?? [], UiText.WorkshopCollections_Published);
        Add(_history.Items, UiText.WorkshopCollections_Remembered);
        return options;

        void Add(IEnumerable<WorkshopEntry> entries, string source)
        {
            foreach (var (id, title) in entries)
            {
                if (!options.Exists(option => option.Id == id))
                {
                    options.Add(new ModpackSteamOption(id, title, source));
                }
            }
        }
    }

    /// <summary>Writes the modpack's own preset copy to the game's presets and links it to that preset.</summary>
    /// <returns>The name it was written under; <c>null</c> when cancelled or the write failed.</returns>
    private async Task<string?> RestorePresetAsync(Modpack modpack)
    {
        if (await ResolvePresetNameAsync(modpack.Preset.Name, UiText.Modpacks_RestoreTitle, UiText.ModCollections_Create) is not { } target)
        {
            return null;
        }

        var written = await Page.WriteAsync(async (root, extraGameArgs) =>
        {
            var saved = await Page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, null, target, modpack.Preset.ModIds));

            // Before the re-read, which would otherwise leave the copy independent.
            if (saved.IsSuccess)
            {
                _modpacks.LinkPreset(root, modpack.Name, target);
            }

            return saved;
        });
        return written ? target : null;
    }

    /// <summary>The name to write a preset under: its own when free, else the same one to overwrite or a new one, as the user chooses.</summary>
    /// <returns><c>null</c> when cancelled.</returns>
    private async Task<string?> ResolvePresetNameAsync(string name, string title, string accept) =>
        !PresetExists(name) ? name
        : await AskOverwriteAsync(UiText.ModCollections_OverwriteTitle, UiText.ModCollections_OverwriteMessage, name) switch
        {
            ConfirmChoice.Confirm => name,
            ConfirmChoice.Alternative =>
                await ModCollectionPrompts.AskNameAsync(Dialogs, ModCollectionPrompts.NamePrompt(title, name, accept), PresetExists) is TextPromptResult.Accepted accepted
                    ? accepted.Text
                    : null,
            ConfirmChoice.Cancel => null,
        };

    /// <summary>Asks whether a name clash overwrites its namesake (<see cref="ConfirmChoice.Confirm"/>) or takes a new name (<see cref="ConfirmChoice.Alternative"/>).</summary>
    private Task<ConfirmChoice> AskOverwriteAsync(string title, string message, string name) =>
        Dialogs.ChooseAsync(
            new ConfirmPrompt(title, Format(message, name), UiText.ModCollections_Overwrite, UiText.Dialog_Cancel),
            UiText.Modpacks_NewName);

    private bool PresetExists(string name) => Page.State?.Collections.Any(collection => collection.Name == name) ?? false;

    /// <summary>Shows the list again, and lists the selected modpack's items again when they aren't the listed ones.</summary>
    /// <returns>The latest load of the items.</returns>
    private Task ShowSelectedAsync()
    {
        RefreshList();
        if (Page.Section == ModsSection.Modpacks && !SameItems(Current, _listed))
        {
            _load = LoadItemsAsync();
        }

        return _load;
    }

    /// <summary>
    /// The selected modpack's collection items, then its extra items, recomputed from the install's mods and saved when they changed: their
    /// latest versions from the Web API, merged with the subscriptions like an opened collection.
    /// </summary>
    private Task LoadItemsAsync() => RunLoadAsync(async version =>
    {
        _listed = Current;
        if (Page.Install is not { } install || _listed is not { } modpack)
        {
            SetItems(null);
            SetRows([]);
            return;
        }

        IReadOnlyList<WorkshopCollectionItem> collectionItems = [];
        Error? collectionError = null;
        switch (modpack.Collection)
        {
            case ModpackCollection.Steam steam:
                var read = await ReadSteamCollectionAsync(steam.Id);
                if (!IsLatest(version))
                {
                    return;
                }

                if (read.IsSuccess)
                {
                    collectionItems = read.Value.Items;
                }
                else
                {
                    collectionError = read.Error;
                }

                break;
            case ModpackCollection.Local local:
                collectionItems = [.. local.Items.Select(item => new WorkshopCollectionItem(item.Id, null))];
                break;
        }

        var collectionIds = collectionItems.Select(item => item.Id).ToHashSet();
        var extras = ComputeExtras(modpack, collectionIds) ?? modpack.ExtraItems;
        if (!extras.SequenceEqual(modpack.ExtraItems))
        {
            // Listed first, so the change it raises doesn't list the items again.
            _listed = modpack with { ExtraItems = extras };
            _modpacks.SetExtraItems(install.RootPath, modpack.Name, extras);
        }

        // An unread Steam collection lists none, and its items may be among the extra ones.
        List<ulong> extraIds = [.. extras.Where(id => !collectionIds.Contains(id))];
        List<ulong> unknown = [.. collectionItems.Where(item => item.Details is null).Select(item => item.Id), .. extraIds];
        Dictionary<ulong, WorkshopItemDetails> latest = [];
        Error? detailsError = null;
        if (unknown.Count > 0)
        {
            var details = await Service.CheckUpdatesAsync(unknown);
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

        SetItems(
        [
            .. collectionItems.Select(item => item.Details is null ? item with { Details = latest.GetValueOrDefault(item.Id) } : item),
            .. extraIds.Select(id => new WorkshopCollectionItem(id, latest.GetValueOrDefault(id))),
        ]);
        await MergeAsync(version);
        if (IsLatest(version))
        {
            foreach (var warning in new[] { collectionError, detailsError }.OfType<Error>())
            {
                AddWarning(warning);
            }
        }
    });

    private async Task<Result<WorkshopCollection>> ReadSteamCollectionAsync(ulong id)
    {
        if (_steamCollections.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var read = await Service.GetCollectionAsync(id);
        if (read.IsSuccess)
        {
            _steamCollections[id] = read.Value;
            Titles.SeenCollections([new WorkshopEntry(id, read.Value.Title)]);
            RefreshList();
        }

        return read;
    }

    /// <summary><c>null</c> while the install's mods are unknown.</summary>
    private IReadOnlyList<ulong>? ComputeExtras(Modpack modpack, IReadOnlyCollection<ulong> collectionIds) =>
        Page.State is { } state ? ModpackExtraItems.Compute(modpack.ExtraItems, modpack.Preset.ModIds, state.InstalledMods, collectionIds) : null;

    // The install's mods may have changed: the shown modpack's extra items are recomputed, and listed again when they changed.
    private void OnPageStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModsViewModel.State))
        {
            return;
        }

        RefreshList();
        if (Page.Section != ModsSection.Modpacks || Page.Install is not { } install || Current is not { } modpack)
        {
            return;
        }

        // Without asking Steam: a Steam collection not read yet is left to the load reading it.
        IReadOnlyCollection<ulong>? collectionIds = modpack.Collection switch
        {
            ModpackCollection.Steam steam => _steamCollections.GetValueOrDefault(steam.Id)?.Items.Select(item => item.Id).ToHashSet(),
            ModpackCollection.Local local => local.Items.Select(item => item.Id).ToHashSet(),
            _ => null,
        };
        if (collectionIds is not null && ComputeExtras(modpack, collectionIds) is { } extras && !extras.SequenceEqual(modpack.ExtraItems))
        {
            _modpacks.SetExtraItems(install.RootPath, modpack.Name, extras);
        }
    }

    private void RefreshList()
    {
        var all = Page.Install is { } install ? _modpacks.For(install.RootPath) : [];
        var selected = all.FirstOrDefault(modpack => modpack.Name == _selectedName) ?? all.FirstOrDefault();
        _selectedName = selected?.Name;
        Modpacks = [.. all.Select(modpack => RowOf(modpack, modpack.Name == _selectedName))];
        Selected = Modpacks.FirstOrDefault(row => row.IsSelected);
    }

    private ModpackRow RowOf(Modpack modpack, bool isSelected)
    {
        var (title, idText, linked) = modpack.Collection switch
        {
            ModpackCollection.Steam steam => (SteamTitle(steam.Id) ?? IdText(steam.Id), IdText(steam.Id), true),
            ModpackCollection.Local local => (local.Name, (string?)null, local.Linked is not null),
            _ => throw new ArgumentException($"Unknown modpack collection {modpack.Collection}.", nameof(modpack)),
        };
        var preset = modpack.Preset;
        return new ModpackRow(modpack.Name, preset.Name, preset.ModIds.Count, preset.IsLinked, title, idText, linked, modpack.ExtraItems.Count, isSelected);
    }

    // Read this session, else as last seen in the opened or starred collections.
    private string? SteamTitle(ulong id) =>
        _steamCollections.GetValueOrDefault(id)?.Title
        ?? _history.Items.FirstOrDefault(entry => entry.Id == id)?.Title
        ?? Favorites.Collections.FirstOrDefault(entry => entry.Id == id)?.Title;

    /// <param name="ReplacesModpack">The install's modpack of this name is overwritten.</param>
    /// <param name="Local">For a local collection; <c>null</c> for a Steam one.</param>
    private sealed record ImportNames(string Modpack, bool ReplacesModpack, string Preset, ImportedLocalName? Local);

    /// <param name="Replaces">The local collection overwritten; <c>null</c> when a new one is made.</param>
    private sealed record ImportedLocalName(string Name, Guid? Replaces);
}
