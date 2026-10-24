using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PzLauncher.Desktop.ViewModels.Mods;

namespace PzLauncher.Desktop.Views.Mods;

/// <summary>
/// Drag reorder of a virtualized list of fixed-height rows, started on a row's <c>dragHandle</c>: lifts a copy of the row into
/// <paramref name="dragFloat"/> and only slides the realized rows with render transforms, so the list isn't rebuilt per step;
/// the move goes to <paramref name="target"/> once the copy has settled into the gap.
/// </summary>
/// <param name="owner">The view holding the list; its theme gives the motion, and leaving the visual tree cancels a drag.</param>
/// <param name="dragFloat">On a canvas over <paramref name="scroller"/>, with the rows' template.</param>
/// <param name="rowHeight">Every row's height, spacing included: the list is virtualized, so the drag counts rows instead of measuring them.</param>
public sealed class ListReorderDrag
{
    private const double LiftScale = 1.02;
    private static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(16);

    private readonly Control _owner;
    private readonly ItemsControl _list;
    private readonly ScrollViewer _scroller;
    private readonly ContentControl _dragFloat;
    private readonly double _rowHeight;
    private readonly Func<IReorderableList?> _target;
    private readonly DispatcherTimer _autoScroll = new() { Interval = AutoScrollInterval };
    private Press? _press;
    private Drag? _drag;

    // Pointer position in the scroller, which doesn't move when the list scrolls.
    private double _pointerY;

    public ListReorderDrag(Control owner, ItemsControl list, ScrollViewer scroller, ContentControl dragFloat, double rowHeight, Func<IReorderableList?> target)
    {
        _owner = owner;
        _list = list;
        _scroller = scroller;
        _dragFloat = dragFloat;
        _rowHeight = rowHeight;
        _target = target;

        // Tunnel: the handle sits inside a row template, so the list sees the press before anything in the row.
        list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        list.PointerMoved += OnPointerMoved;
        list.PointerReleased += OnPointerReleased;
        list.PointerCaptureLost += OnPointerCaptureLost;
        list.ContainerPrepared += OnContainerPrepared;
        list.ContainerClearing += OnContainerClearing;
        list.PropertyChanged += OnListPropertyChanged;
        scroller.ScrollChanged += (_, _) => Follow();
        owner.DetachedFromVisualTree += (_, _) => Cancel();
        _autoScroll.Tick += (_, _) => AutoScroll();
    }

    private void Cancel()
    {
        _press = null;
        if (_drag is { } drag)
        {
            End(drag, commit: false);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_press is not null
            || _drag is not null
            || _target() is not { CanReorder: true }
            || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed
            || HandleAt(e.Source) is not { DataContext: { } item })
        {
            return;
        }

        _press = new Press(item, e.Pointer, e.GetPosition(_scroller).Y);
        e.Pointer.Capture(_list);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var y = e.GetPosition(_scroller).Y;
        if (_press is { } press)
        {
            if (!ListReorderLayout.IsLift(press.ViewportY, y))
            {
                return;
            }

            _press = null;
            Lift(press);
        }

        _pointerY = y;
        Follow();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_press is not null)
        {
            _press = null;
            e.Pointer.Capture(null);
        }
        else if (_drag is { IsSettling: false } drag)
        {
            Settle(drag, drag.Target);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _press = null;
        if (_drag is { IsSettling: false } drag)
        {
            Settle(drag, drag.From);
        }
    }

    private void OnDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is { IsSettling: false } drag)
        {
            e.Handled = true;
            Settle(drag, drag.From);
        }
    }

    // Whatever replaced the rows (a reload, another list) wins over the drag.
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_drag is { } drag)
        {
            End(drag, commit: false);
        }
    }

    private void OnListPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ItemsControl.ItemsSourceProperty && _drag is { } drag)
        {
            End(drag, commit: false);
        }
    }

    // A row realized during a drag (scrolled into view, recycled) takes its slot's offset, or the gap's hiding.
    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_drag is { } drag)
        {
            Track(drag, e.Container, e.Index);
        }
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (_drag is { } drag && drag.Shifts.Remove(e.Container))
        {
            Untrack(e.Container);
        }
    }

    private void Lift(Press press)
    {
        if (_target() is not { } target
            || _list.Items.IndexOf(press.Item) is not (>= 0 and var from)
            || _list.ItemsPanelRoot is not { } panel
            || panel.TranslatePoint(default, _scroller) is not { } origin)
        {
            ReleaseCapture(press.Pointer);
            return;
        }

        var layout = new ListReorderLayout(_list.ItemCount, _rowHeight, origin.Y + _scroller.Offset.Y);
        var (duration, easing) = Motion();
        var drag = new Drag(target, press.Pointer, layout, from, duration, easing)
        {
            GrabOffset = press.ViewportY - origin.Y - layout.TopOf(from),
            KeyScope = TopLevel.GetTopLevel(_owner),
        };
        _drag = drag;

        foreach (var row in _list.GetRealizedContainers().ToArray())
        {
            Track(drag, row, _list.IndexFromContainer(row));
        }

        _dragFloat.Content = press.Item;
        _dragFloat.Width = panel.Bounds.Width;
        Canvas.SetLeft(_dragFloat, origin.X);
        Canvas.SetTop(_dragFloat, origin.Y + layout.TopOf(from));
        _dragFloat.RenderTransform = Float(0, 1);
        _dragFloat.Transitions = Slides(drag);
        _dragFloat.IsVisible = true;
        _dragFloat.RenderTransform = Float(0, LiftScale);

        _list.Items.CollectionChanged += OnItemsChanged;
        drag.KeyScope?.AddHandler(InputElement.KeyDownEvent, OnDragKeyDown, RoutingStrategies.Tunnel);
        _autoScroll.Start();
    }

    private void Follow()
    {
        if (_drag is not { IsSettling: false } drag)
        {
            return;
        }

        var scroll = _scroller.Offset.Y;
        var top = drag.Layout.FloatTop(_pointerY - drag.GrabOffset, scroll, _scroller.Viewport.Height);
        Canvas.SetTop(_dragFloat, top);

        var target = drag.Layout.TargetIndex(drag.From, top, scroll);
        if (target != drag.Target)
        {
            drag.Target = target;
            SlideRows(drag);
        }
    }

    private void AutoScroll()
    {
        if (_drag is not { IsSettling: false } drag)
        {
            return;
        }

        var offset = _scroller.Offset;
        var y = drag.Layout.AutoScrollOffset(offset.Y, _pointerY, _scroller.Viewport.Height);
        if (y != offset.Y)
        {
            _scroller.Offset = offset.WithY(y);
            Follow();
        }
    }

    /// <param name="to">Where the row lands: the gap on a drop, its own slot on a cancel.</param>
    private void Settle(Drag drag, int to)
    {
        drag.IsSettling = true;
        _autoScroll.Stop();
        ReleaseCapture(drag.Pointer);
        drag.Target = to;
        SlideRows(drag);

        var landing = drag.Layout.LandingTop(to, _scroller.Offset.Y, _scroller.Viewport.Height);
        _dragFloat.RenderTransform = Float(landing - Canvas.GetTop(_dragFloat), 1);
        drag.Settled = DispatcherTimer.RunOnce(() => End(drag, commit: true), drag.Duration);
    }

    private void End(Drag drag, bool commit)
    {
        if (_drag != drag)
        {
            return;
        }

        _drag = null;
        _autoScroll.Stop();
        drag.Settled?.Dispose();
        ReleaseCapture(drag.Pointer);
        _list.Items.CollectionChanged -= OnItemsChanged;
        drag.KeyScope?.RemoveHandler(InputElement.KeyDownEvent, OnDragKeyDown);

        foreach (var row in drag.Shifts.Keys)
        {
            Untrack(row);
        }

        drag.Shifts.Clear();
        _dragFloat.Transitions = null;
        _dragFloat.RenderTransform = null;
        _dragFloat.IsVisible = false;
        _dragFloat.Content = null;

        if (commit)
        {
            drag.List.Move(drag.From, drag.Target);
        }
    }

    private void SlideRows(Drag drag)
    {
        foreach (var (row, current) in drag.Shifts.ToArray())
        {
            var index = _list.IndexFromContainer(row);
            var shift = index < 0 ? current : drag.Layout.ShiftOf(index, drag.From, drag.Target);
            if (shift != current)
            {
                drag.Shifts[row] = shift;
                row.RenderTransform = Slide(shift);
            }
        }
    }

    // The first offset goes on without transitions, so a row realized mid-drag doesn't slide in from its own slot.
    private static void Track(Drag drag, Control row, int index)
    {
        if (index < 0)
        {
            return;
        }

        var shift = drag.Layout.ShiftOf(index, drag.From, drag.Target);
        row.Transitions = null;
        row.RenderTransform = Slide(shift);
        row.Transitions = Slides(drag);
        if (index == drag.From)
        {
            row.Opacity = 0;
        }
        else
        {
            row.ClearValue(Visual.OpacityProperty);
        }

        drag.Shifts[row] = shift;
    }

    // Transitions go first, so the row jumps back to its slot instead of sliding there.
    private static void Untrack(Control row)
    {
        row.Transitions = null;
        row.RenderTransform = null;
        row.ClearValue(Visual.OpacityProperty);
    }

    private static Transitions Slides(Drag drag) =>
    [
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = drag.Duration, Easing = drag.Easing },
    ];

    // Same operation list in every state, so the transition interpolates each operation instead of a matrix.
    private static TransformOperations Slide(double y)
    {
        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendTranslate(0, y);
        return builder.Build();
    }

    private static TransformOperations Float(double y, double scale)
    {
        var builder = TransformOperations.CreateBuilder(2);
        builder.AppendTranslate(0, y);
        builder.AppendScale(scale, scale);
        return builder.Build();
    }

    private (TimeSpan Duration, Easing Easing) Motion()
    {
        var theme = _owner.ActualThemeVariant;
        var duration = _owner.TryFindResource("Shell.StateTransitionDuration", theme, out var value) && value is TimeSpan time ? time : TimeSpan.Zero;
        var easing = _owner.TryFindResource("Shell.MotionEasing", theme, out value) && value is Easing found ? found : new LinearEasing();
        return (duration, easing);
    }

    // A press elsewhere during the settle animation owns the pointer by now.
    private void ReleaseCapture(IPointer pointer)
    {
        if (pointer.Captured == _list)
        {
            pointer.Capture(null);
        }
    }

    private Control? HandleAt(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != _list; visual = visual.GetVisualParent())
        {
            if (visual is Control control && control.Classes.Contains("dragHandle"))
            {
                return control;
            }
        }

        return null;
    }

    private sealed record Press(object Item, IPointer Pointer, double ViewportY);

    private sealed class Drag(IReorderableList target, IPointer pointer, ListReorderLayout layout, int from, TimeSpan duration, Easing easing)
    {
        public IReorderableList List { get; } = target;

        public IPointer Pointer { get; } = pointer;

        public ListReorderLayout Layout { get; } = layout;

        public int From { get; } = from;

        public TimeSpan Duration { get; } = duration;

        public Easing Easing { get; } = easing;

        /// <summary>Pointer distance below the dragged row's top.</summary>
        public double GrabOffset { get; init; }

        public TopLevel? KeyScope { get; init; }

        public int Target { get; set; } = from;

        /// <summary>The realized containers the drag has offset, with each one's current offset.</summary>
        public Dictionary<Control, double> Shifts { get; } = new();

        public bool IsSettling { get; set; }

        public IDisposable? Settled { get; set; }
    }
}
