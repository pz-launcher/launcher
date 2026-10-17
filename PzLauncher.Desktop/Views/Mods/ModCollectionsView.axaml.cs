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

// A drag lifts a copy of the row into DragLayer and only slides the realized rows with render transforms,
// so the list isn't rebuilt per step; the move is committed once the copy has settled into the gap.
public partial class ModCollectionsView : UserControl
{
    /// <summary>Every entry row's height, spacing included: the list is virtualized, so the drag counts rows instead of measuring them.</summary>
    public const double EntryRowHeight = 44;

    private const double LiftScale = 1.02;
    private static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _autoScroll = new() { Interval = AutoScrollInterval };
    private Press? _press;
    private Drag? _drag;

    // Pointer position in EntriesScroller, which doesn't move when the list scrolls.
    private double _pointerY;

    public ModCollectionsView()
    {
        InitializeComponent();

        // Tunnel: the handle sits inside a row template, so the list sees the press before anything in the row.
        EntriesList.AddHandler(PointerPressedEvent, OnEntriesPointerPressed, RoutingStrategies.Tunnel);
        EntriesList.PointerMoved += OnEntriesPointerMoved;
        EntriesList.PointerReleased += OnEntriesPointerReleased;
        EntriesList.PointerCaptureLost += OnEntriesPointerCaptureLost;
        EntriesList.ContainerPrepared += OnEntryContainerPrepared;
        EntriesList.ContainerClearing += OnEntryContainerClearing;
        EntriesScroller.ScrollChanged += (_, _) => Follow();
        _autoScroll.Tick += (_, _) => AutoScroll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _press = null;
        if (_drag is { } drag)
        {
            End(drag, commit: false);
        }
    }

    private void OnEntriesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_press is not null
            || _drag is not null
            || DataContext is not ModCollectionsViewModel { CanReorder: true }
            || !e.GetCurrentPoint(EntriesList).Properties.IsLeftButtonPressed
            || HandleAt(e.Source) is not { DataContext: ModCollectionEntryRow entry })
        {
            return;
        }

        _press = new Press(entry, e.Pointer, e.GetPosition(EntriesScroller).Y);
        e.Pointer.Capture(EntriesList);
        e.Handled = true;
    }

    private void OnEntriesPointerMoved(object? sender, PointerEventArgs e)
    {
        var y = e.GetPosition(EntriesScroller).Y;
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

    private void OnEntriesPointerReleased(object? sender, PointerReleasedEventArgs e)
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

    private void OnEntriesPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
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

    // Whatever replaced the rows (a reload, another collection) wins over the drag.
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_drag is { } drag)
        {
            End(drag, commit: false);
        }
    }

    // A row realized during a drag (scrolled into view, recycled) takes its slot's offset, or the gap's hiding.
    private void OnEntryContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_drag is { } drag)
        {
            Track(drag, e.Container, e.Index);
        }
    }

    private void OnEntryContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (_drag is { } drag && drag.Shifts.Remove(e.Container))
        {
            Untrack(e.Container);
        }
    }

    private void Lift(Press press)
    {
        if (DataContext is not ModCollectionsViewModel vm
            || vm.Entries.IndexOf(press.Entry) is not (>= 0 and var from)
            || EntriesList.ItemsPanelRoot is not { } panel
            || panel.TranslatePoint(default, EntriesScroller) is not { } origin)
        {
            ReleaseCapture(press.Pointer);
            return;
        }

        var layout = new ListReorderLayout(vm.Entries.Count, EntryRowHeight, origin.Y + EntriesScroller.Offset.Y);
        var (duration, easing) = Motion();
        var drag = new Drag(vm, press.Pointer, layout, from, duration, easing)
        {
            GrabOffset = press.ViewportY - origin.Y - layout.TopOf(from),
            KeyScope = TopLevel.GetTopLevel(this),
        };
        _drag = drag;

        foreach (var row in EntriesList.GetRealizedContainers().ToArray())
        {
            Track(drag, row, EntriesList.IndexFromContainer(row));
        }

        DragFloat.Content = press.Entry;
        DragFloat.Width = panel.Bounds.Width;
        Canvas.SetLeft(DragFloat, origin.X);
        Canvas.SetTop(DragFloat, origin.Y + layout.TopOf(from));
        DragFloat.RenderTransform = Float(0, 1);
        DragFloat.Transitions = Slides(drag);
        DragFloat.IsVisible = true;
        DragFloat.RenderTransform = Float(0, LiftScale);

        vm.Entries.CollectionChanged += OnEntriesChanged;
        drag.KeyScope?.AddHandler(KeyDownEvent, OnDragKeyDown, RoutingStrategies.Tunnel);
        _autoScroll.Start();
    }

    private void Follow()
    {
        if (_drag is not { IsSettling: false } drag)
        {
            return;
        }

        var scroll = EntriesScroller.Offset.Y;
        var top = drag.Layout.FloatTop(_pointerY - drag.GrabOffset, scroll, EntriesScroller.Viewport.Height);
        Canvas.SetTop(DragFloat, top);

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

        var offset = EntriesScroller.Offset;
        var y = drag.Layout.AutoScrollOffset(offset.Y, _pointerY, EntriesScroller.Viewport.Height);
        if (y != offset.Y)
        {
            EntriesScroller.Offset = offset.WithY(y);
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

        var landing = drag.Layout.LandingTop(to, EntriesScroller.Offset.Y, EntriesScroller.Viewport.Height);
        DragFloat.RenderTransform = Float(landing - Canvas.GetTop(DragFloat), 1);
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
        drag.Vm.Entries.CollectionChanged -= OnEntriesChanged;
        drag.KeyScope?.RemoveHandler(KeyDownEvent, OnDragKeyDown);

        foreach (var row in drag.Shifts.Keys)
        {
            Untrack(row);
        }

        drag.Shifts.Clear();
        DragFloat.Transitions = null;
        DragFloat.RenderTransform = null;
        DragFloat.IsVisible = false;
        DragFloat.Content = null;

        if (commit)
        {
            drag.Vm.Move(drag.From, drag.Target);
        }
    }

    private void SlideRows(Drag drag)
    {
        foreach (var (row, current) in drag.Shifts.ToArray())
        {
            var index = EntriesList.IndexFromContainer(row);
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
            row.ClearValue(OpacityProperty);
        }

        drag.Shifts[row] = shift;
    }

    // Transitions go first, so the row jumps back to its slot instead of sliding there.
    private static void Untrack(Control row)
    {
        row.Transitions = null;
        row.RenderTransform = null;
        row.ClearValue(OpacityProperty);
    }

    private static Transitions Slides(Drag drag) =>
    [
        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = drag.Duration, Easing = drag.Easing },
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
        var duration = this.TryFindResource("Shell.StateTransitionDuration", ActualThemeVariant, out var value) && value is TimeSpan time ? time : TimeSpan.Zero;
        var easing = this.TryFindResource("Shell.MotionEasing", ActualThemeVariant, out value) && value is Easing found ? found : new LinearEasing();
        return (duration, easing);
    }

    // A press elsewhere during the settle animation owns the pointer by now.
    private void ReleaseCapture(IPointer pointer)
    {
        if (pointer.Captured == EntriesList)
        {
            pointer.Capture(null);
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

    private sealed record Press(ModCollectionEntryRow Entry, IPointer Pointer, double ViewportY);

    private sealed class Drag(ModCollectionsViewModel vm, IPointer pointer, ListReorderLayout layout, int from, TimeSpan duration, Easing easing)
    {
        public ModCollectionsViewModel Vm { get; } = vm;

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
