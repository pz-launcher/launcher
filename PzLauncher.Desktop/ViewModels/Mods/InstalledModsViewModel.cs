using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>Browses installed mods and adds them to collections; never touches the enabled list.</summary>
public partial class InstalledModsViewModel : ViewModelBase
{
    private readonly ModsViewModel _page;

    // By id, so a reload keeps what was ticked.
    private readonly HashSet<string> _selectedIds = new(StringComparer.Ordinal);
    private IReadOnlyList<InstalledModRow> _rows = [];

    internal InstalledModsViewModel(ModsViewModel page)
    {
        _page = page;
        VisibleRows = [];
        AddTargets = [];
        page.PropertyChanged += OnPagePropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterEmpty))]
    public partial IReadOnlyList<InstalledModRow> VisibleRows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCollection))]
    public partial IReadOnlyList<ModCollectionTarget> AddTargets { get; private set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty] public partial InstalledModsFilter Filter { get; set; }

    public int SelectedCount => _selectedIds.Count;

    public bool CanAddToCollection => _page.CanWrite && SelectedCount > 0 && AddTargets.Count > 0;

    /// <summary>Loaded, and no mod is installed.</summary>
    public bool IsEmpty => _page.State is not null && _rows.Count == 0;

    public bool IsFilterEmpty => _rows.Count > 0 && VisibleRows.Count == 0;

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshVisibleRows();

    partial void OnFilterChanged(InstalledModsFilter value) => RefreshVisibleRows();

    private async Task AddToAsync(string name)
    {
        if (!CanAddToCollection || _page.State?.Collections.FirstOrDefault(collection => collection.Name == name) is not { } collection)
        {
            return;
        }

        // Appended in the list's order; ids already in the collection keep their place.
        var added = _rows.Where(row => row.IsSelected).Select(row => row.Mod.Id).ToList();
        IReadOnlyList<string> ids = [.. collection.ModIds.Concat(added).Distinct(StringComparer.Ordinal)];
        if (ids.Count == collection.ModIds.Count)
        {
            ClearSelection();
            return;
        }

        if (await _page.WriteAsync((root, extraGameArgs) =>
                _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, name, name, ids))))
        {
            ClearSelection();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ModsViewModel.State):
                Rebuild(_page.State);
                break;
            case nameof(ModsViewModel.CanWrite):
                OnPropertyChanged(nameof(CanAddToCollection));
                break;
        }
    }

    private void Rebuild(ModCollectionState? state)
    {
        if (state is null)
        {
            _rows = [];
            AddTargets = [];
            RefreshVisibleRows();
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        var enabled = state.ActiveIds.ToHashSet(StringComparer.Ordinal);
        var installedIds = state.InstalledMods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        _selectedIds.IntersectWith(installedIds);
        _rows =
        [
            .. state.InstalledMods
                .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(mod => mod.Id, StringComparer.Ordinal)
                .Select(mod => new InstalledModRow(
                    mod,
                    enabled.Contains(mod.Id),
                    string.Join(", ", state.Collections.Where(collection => collection.ModIds.Contains(mod.Id)).Select(collection => collection.Name)),
                    _selectedIds.Contains(mod.Id),
                    OnRowSelectionChanged)),
        ];
        AddTargets =
        [
            .. state.Collections.Select(collection =>
                new ModCollectionTarget(collection.Name, new AsyncRelayCommand(() => AddToAsync(collection.Name)))),
        ];
        RefreshVisibleRows();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanAddToCollection));
    }

    private void OnRowSelectionChanged(InstalledModRow row)
    {
        if (row.IsSelected)
        {
            _selectedIds.Add(row.Mod.Id);
        }
        else
        {
            _selectedIds.Remove(row.Mod.Id);
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanAddToCollection));
    }

    private void RefreshVisibleRows()
    {
        var search = SearchText.Trim();
        VisibleRows = [.. _rows.Where(row => MatchesFilter(row) && Matches(row.Mod, search))];
    }

    private bool MatchesFilter(InstalledModRow row) => Filter switch
    {
        InstalledModsFilter.All => true,
        InstalledModsFilter.Enabled => row.IsEnabled,
        InstalledModsFilter.NotEnabled => !row.IsEnabled,
    };

    /// <summary>Name, id or Workshop id contains the search text, ignoring case; an empty search matches all.</summary>
    internal static bool Matches(InstalledMod mod, string search) =>
        search.Length == 0
        || mod.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (mod.WorkshopId?.Contains(search, StringComparison.Ordinal) ?? false);
}
