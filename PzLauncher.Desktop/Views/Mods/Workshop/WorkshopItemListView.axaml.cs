using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;

namespace PzLauncher.Desktop.Views.Mods.Workshop;

public partial class WorkshopItemListView : UserControl
{
    public static readonly StyledProperty<IEnumerable<WorkshopItemRow>?> RowsProperty =
        AvaloniaProperty.Register<WorkshopItemListView, IEnumerable<WorkshopItemRow>?>(nameof(Rows));

    public static readonly StyledProperty<bool> ShowsSelectionProperty = AvaloniaProperty.Register<WorkshopItemListView, bool>(nameof(ShowsSelection));

    public static readonly StyledProperty<bool> ShowsUpdatedProperty = AvaloniaProperty.Register<WorkshopItemListView, bool>(nameof(ShowsUpdated));

    public static readonly StyledProperty<object?> ColumnHeaderProperty = AvaloniaProperty.Register<WorkshopItemListView, object?>(nameof(ColumnHeader));

    public WorkshopItemListView()
    {
        InitializeComponent();
    }

    public IEnumerable<WorkshopItemRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Each row starts with its selection check box.</summary>
    public bool ShowsSelection
    {
        get => GetValue(ShowsSelectionProperty);
        set => SetValue(ShowsSelectionProperty, value);
    }

    /// <summary>Each row shows when its item was updated.</summary>
    public bool ShowsUpdated
    {
        get => GetValue(ShowsUpdatedProperty);
        set => SetValue(ShowsUpdatedProperty, value);
    }

    /// <summary>Replaces the plain column titles, e.g. with sort buttons; laid out on a <see cref="WorkshopItemGrid"/>.</summary>
    public object? ColumnHeader
    {
        get => GetValue(ColumnHeaderProperty);
        set => SetValue(ColumnHeaderProperty, value);
    }
}
