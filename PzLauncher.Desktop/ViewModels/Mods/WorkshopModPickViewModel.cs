using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>
/// One per dialog before a launcher mod collection is made from a Steam Workshop collection: a checkbox per mod of the
/// items that hold several, and the items left out.
/// </summary>
/// <param name="skipped">Each item left out, with the reason.</param>
public sealed partial class WorkshopModPickViewModel(IReadOnlyList<WorkshopModPickItem> items, IReadOnlyList<string> skipped) : ViewModelBase
{
    /// <summary><c>true</c> for the accept button.</summary>
    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<WorkshopModPickItem> Items { get; } = items;

    public IReadOnlyList<string> Skipped { get; } = skipped;

    public bool HasItems => Items.Count > 0;

    public bool HasSkipped => Skipped.Count > 0;

    public string SkippedText => string.Join(Environment.NewLine, Skipped);

    [RelayCommand]
    private void Accept() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}

/// <param name="Name">The item's title with its id.</param>
/// <param name="Mods">In the item folder's order.</param>
public sealed record WorkshopModPickItem(ulong Id, string Name, IReadOnlyList<WorkshopModChoice> Mods);

/// <param name="conflicts">Ids of other mods the new collection would hold that this one is incompatible with, either way round.</param>
public sealed partial class WorkshopModChoice(InstalledMod mod, IReadOnlyList<string> conflicts) : ObservableObject
{
    public InstalledMod Mod { get; } = mod;

    public bool IsConflicting => conflicts.Count > 0;

    public string? ConflictText => IsConflicting
        ? string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_PickConflict, string.Join(", ", conflicts))
        : null;

    [ObservableProperty] public partial bool IsChecked { get; set; } = true;
}
