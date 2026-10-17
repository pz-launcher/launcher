using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PzLauncher.Desktop.ViewModels.Mods;

namespace PzLauncher.Desktop.Views.Mods;

public partial class ModCollectionsView : UserControl
{
    private ModCollectionEntryRow? _dragged;

    public ModCollectionsView()
    {
        InitializeComponent();

        // Tunnel: the handle sits inside a row template, so the list sees the press before anything in the row.
        EntriesList.AddHandler(PointerPressedEvent, OnEntriesPointerPressed, RoutingStrategies.Tunnel);
        EntriesList.PointerMoved += OnEntriesPointerMoved;
        EntriesList.PointerReleased += OnEntriesPointerReleased;
        EntriesList.PointerCaptureLost += (_, _) => _dragged = null;
    }

    private void OnEntriesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(EntriesList).Properties.IsLeftButtonPressed || HandleAt(e.Source) is not { DataContext: ModCollectionEntryRow entry })
        {
            return;
        }

        _dragged = entry;

        // Captured by the list, not the handle: a move re-creates the dragged row's container, which would drop the capture.
        e.Pointer.Capture(EntriesList);
        e.Handled = true;
    }

    private void OnEntriesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || DataContext is not ModCollectionsViewModel vm)
        {
            return;
        }

        var from = vm.Entries.IndexOf(_dragged);
        var to = IndexAt(e.GetPosition(EntriesList).Y);
        if (from >= 0 && to >= 0 && from != to)
        {
            vm.Move(from, to);
        }
    }

    private void OnEntriesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is not null)
        {
            _dragged = null;
            e.Pointer.Capture(null);
        }
    }

    private Control? HandleAt(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != EntriesList; visual = visual.GetVisualParent())
        {
            if (visual is Control control && control.Classes.Contains("dragHandle"))
            {
                return control;
            }
        }

        return null;
    }

    // The row under the pointer; above the first row is the first, below the last is the last.
    private int IndexAt(double y)
    {
        var count = EntriesList.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (EntriesList.ContainerFromIndex(i) is { } container
                && container.TranslatePoint(default, EntriesList) is { } top
                && y < top.Y + container.Bounds.Height)
            {
                return i;
            }
        }

        return count - 1;
    }
}
