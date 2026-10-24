using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;

namespace PzLauncher.Desktop.Views.Mods.Workshop;

public partial class WorkshopItemListView : UserControl
{
    /// <summary>Every row's height, spacing included, while <see cref="Reorder"/> is set: the drag counts rows instead of measuring them.</summary>
    public const double ReorderRowHeight = 50;

    /// <summary>The add-to-local-collection column, in rows and header alike.</summary>
    public const double AddColumnWidth = 40;

    public static readonly StyledProperty<IEnumerable<WorkshopItemRow>?> RowsProperty =
        AvaloniaProperty.Register<WorkshopItemListView, IEnumerable<WorkshopItemRow>?>(nameof(Rows));

    public static readonly StyledProperty<bool> ShowsSelectionProperty = AvaloniaProperty.Register<WorkshopItemListView, bool>(nameof(ShowsSelection));

    public static readonly StyledProperty<bool> ShowsUpdatedProperty = AvaloniaProperty.Register<WorkshopItemListView, bool>(nameof(ShowsUpdated));

    public static readonly StyledProperty<bool> ShowsAddToLocalCollectionProperty =
        AvaloniaProperty.Register<WorkshopItemListView, bool>(nameof(ShowsAddToLocalCollection));

    public static readonly StyledProperty<object?> ColumnHeaderProperty = AvaloniaProperty.Register<WorkshopItemListView, object?>(nameof(ColumnHeader));

    public static readonly StyledProperty<IReorderableList?> ReorderProperty =
        AvaloniaProperty.Register<WorkshopItemListView, IReorderableList?>(nameof(Reorder));

    public static readonly StyledProperty<IDataTemplate?> RowActionsProperty =
        AvaloniaProperty.Register<WorkshopItemListView, IDataTemplate?>(nameof(RowActions));

    public static readonly StyledProperty<double> RowActionsWidthProperty = AvaloniaProperty.Register<WorkshopItemListView, double>(nameof(RowActionsWidth));

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<WorkshopItemListView, double>(nameof(RowHeight), double.NaN);

    public WorkshopItemListView()
    {
        InitializeComponent();
        _ = new ListReorderDrag(this, ItemsList, ItemsScroller, DragFloat, ReorderRowHeight, () => Reorder);
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

    /// <summary>Each row ends with a button adding its item to a local collection.</summary>
    public bool ShowsAddToLocalCollection
    {
        get => GetValue(ShowsAddToLocalCollectionProperty);
        set => SetValue(ShowsAddToLocalCollectionProperty, value);
    }

    /// <summary>Replaces the plain column titles, e.g. with sort buttons; laid out on a <see cref="WorkshopItemGrid"/>.</summary>
    public object? ColumnHeader
    {
        get => GetValue(ColumnHeaderProperty);
        set => SetValue(ColumnHeaderProperty, value);
    }

    /// <summary>Rows start with a drag handle and keep a fixed height; a drop moves the row through this list.</summary>
    public IReorderableList? Reorder
    {
        get => GetValue(ReorderProperty);
        set => SetValue(ReorderProperty, value);
    }

    /// <summary>The section's own buttons at each row's end, given the row; <see cref="RowActionsWidth"/> wide.</summary>
    public IDataTemplate? RowActions
    {
        get => GetValue(RowActionsProperty);
        set => SetValue(RowActionsProperty, value);
    }

    /// <summary>Fixed, so the header lines up with the rows.</summary>
    public double RowActionsWidth
    {
        get => GetValue(RowActionsWidthProperty);
        set => SetValue(RowActionsWidthProperty, value);
    }

    /// <summary><see cref="ReorderRowHeight"/> while <see cref="Reorder"/> is set, else each row's own.</summary>
    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        private set => SetValue(RowHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReorderProperty)
        {
            RowHeight = Reorder is null ? double.NaN : ReorderRowHeight;
        }
    }
}
