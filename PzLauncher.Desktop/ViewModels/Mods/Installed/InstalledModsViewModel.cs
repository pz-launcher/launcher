using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.ViewModels.Mods.Collections;

namespace PzLauncher.Desktop.ViewModels.Mods.Installed;

/// <summary>Browses installed mods and adds them to collections; never touches the enabled list.</summary>
public partial class InstalledModsViewModel : ViewModelBase
{
    private readonly ModsViewModel _page;

    private readonly RowSelection<string> _selection = new(StringComparer.Ordinal);
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

    [ObservableProperty] public partial InstalledModsSortColumn SortColumn { get; private set; }

    [ObservableProperty] public partial bool SortDescending { get; private set; }

    public int SelectedCount => _selection.Count;

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

    [RelayCommand]
    private void SortBy(InstalledModsSortColumn column)
    {
        SortDescending = column == SortColumn && !SortDescending;
        SortColumn = column;
        _rows = Sorted(_rows);
        RefreshVisibleRows();
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
        _selection.IntersectWith(installedIds);
        _rows =
        [
            .. state.InstalledMods.Select(mod => new InstalledModRow(
                mod,
                enabled.Contains(mod.Id),
                string.Join(", ", state.Collections.Where(collection => collection.ModIds.Contains(mod.Id)).Select(collection => collection.Name)),
                _selection.Contains(mod.Id),
                OnRowSelectionChanged)),
        ];
        _rows = Sorted(_rows);
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
        _selection.Set(row.Mod.Id, row.IsSelected);
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanAddToCollection));
    }

    private void RefreshVisibleRows()
    {
        var search = SearchText.Trim();
        VisibleRows = [.. _rows.Where(row => MatchesFilter(row) && Matches(row.Mod, search))];
    }

    private InstalledModRow[] Sorted(IEnumerable<InstalledModRow> rows) =>
        SortedRows.Sort(rows, SortColumn, SortDescending, IsBlank, CompareColumn, CompareByName);

    private static int CompareByName(InstalledModRow x, InstalledModRow y)
    {
        var order = StringComparer.CurrentCultureIgnoreCase.Compare(x.Mod.Name, y.Mod.Name);
        return order != 0 ? order : StringComparer.Ordinal.Compare(x.Mod.Id, y.Mod.Id);
    }

    private static bool IsBlank(InstalledModRow row, InstalledModsSortColumn column) => column switch
    {
        InstalledModsSortColumn.Name => false,
        InstalledModsSortColumn.Id => false,
        InstalledModsSortColumn.WorkshopId => row.Mod.WorkshopId is null,
        InstalledModsSortColumn.Source => false,
        InstalledModsSortColumn.Enabled => false,
        InstalledModsSortColumn.Collections => row.CollectionNames.Length == 0,
    };

    private static int CompareColumn(InstalledModRow x, InstalledModRow y, InstalledModsSortColumn column) => column switch
    {
        InstalledModsSortColumn.Name => StringComparer.CurrentCultureIgnoreCase.Compare(x.Mod.Name, y.Mod.Name),
        InstalledModsSortColumn.Id => StringComparer.CurrentCultureIgnoreCase.Compare(x.Mod.Id, y.Mod.Id),
        InstalledModsSortColumn.WorkshopId => Nullable.Compare(x.Mod.WorkshopId, y.Mod.WorkshopId),
        InstalledModsSortColumn.Source => x.Mod.Source.CompareTo(y.Mod.Source),
        InstalledModsSortColumn.Enabled => y.IsEnabled.CompareTo(x.IsEnabled),
        InstalledModsSortColumn.Collections => StringComparer.CurrentCultureIgnoreCase.Compare(x.CollectionNames, y.CollectionNames),
    };

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
        || (mod.WorkshopId?.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal) ?? false);
}
