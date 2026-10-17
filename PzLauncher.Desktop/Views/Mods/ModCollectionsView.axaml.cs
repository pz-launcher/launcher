using System;
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

// A drag lifts a copy of the row into DragLayer and only slides the real rows with render transforms,
// so the list isn't rebuilt per step; the move is committed once the copy has settled into the gap.
public partial class ModCollectionsView : UserControl
{
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

    private void Lift(Press press)
    {
        if (DataContext is not ModCollectionsViewModel vm
            || vm.Entries.IndexOf(press.Entry) is not (>= 0 and var from)
            || EntriesList.ItemsPanelRoot?.TranslatePoint(default, EntriesScroller) is not { } origin
            || RowContainers(vm.Entries.Count) is not { } rows)
        {
            ReleaseCapture(press.Pointer);
            return;
        }

        var layout = new ListReorderLayout([.. rows.Select(row => row.Bounds.Height)]);
        var (duration, easing) = Motion();
        var drag = new Drag(vm, press.Pointer, rows, layout, from, duration)
        {
            GrabOffset = press.ViewportY - origin.Y - layout.TopOf(from),
            PanelTopInExtent = origin.Y + EntriesScroller.Offset.Y,
            KeyScope = TopLevel.GetTopLevel(this),
        };
        _drag = drag;

        Transitions Slides() =>
        [
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = easing },
        ];
        foreach (var row in rows)
        {
            row.RenderTransform = Slide(0);
            row.Transitions = Slides();
        }

        rows[from].Opacity = 0;

        DragFloat.Content = press.Entry;
        DragFloat.Width = rows[from].Bounds.Width;
        Canvas.SetLeft(DragFloat, origin.X + rows[from].Bounds.X);
        Canvas.SetTop(DragFloat, origin.Y + layout.TopOf(from));
        DragFloat.RenderTransform = Float(0, 1);
        DragFloat.Transitions = Slides();
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

        var (min, max) = VisibleListRange(drag);
        var height = drag.Layout.HeightOf(drag.From);
        var top = ListReorderLayout.ClampTop(_pointerY - drag.GrabOffset, height, min, max);
        Canvas.SetTop(DragFloat, top);

        var panelTop = drag.PanelTopInExtent - EntriesScroller.Offset.Y;
        var target = drag.Layout.TargetIndex(drag.From, top - panelTop + (height / 2));
        if (target != drag.Target)
        {
            drag.Target = target;
            SlideRows(drag);
        }
    }

    private void AutoScroll()
    {
        if (_drag is not { IsSettling: false })
        {
            return;
        }

        var offset = EntriesScroller.Offset;
        var maxY = Math.Max(0, EntriesScroller.Extent.Height - EntriesScroller.Viewport.Height);
        var y = Math.Clamp(offset.Y + ListReorderLayout.AutoScrollStep(_pointerY, EntriesScroller.Viewport.Height), 0, maxY);
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

        // A slot scrolled out of view is approached up to the list's edge, never over the rest of the page.
        var (min, max) = VisibleListRange(drag);
        var panelTop = drag.PanelTopInExtent - EntriesScroller.Offset.Y;
        var landing = ListReorderLayout.ClampTop(panelTop + drag.Layout.LandingTop(drag.From, to), drag.Layout.HeightOf(drag.From), min, max);
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

        // Transitions go first, so the rows jump back to their slots instead of sliding there.
        foreach (var row in drag.Rows)
        {
            row.Transitions = null;
            row.RenderTransform = null;
        }

        drag.Rows[drag.From].ClearValue(OpacityProperty);
        DragFloat.Transitions = null;
        DragFloat.RenderTransform = null;
        DragFloat.IsVisible = false;
        DragFloat.Content = null;

        if (commit)
        {
            drag.Vm.Move(drag.From, drag.Target);
        }
    }

    // The part of DragLayer the list covers: the viewport, or less when the list is shorter.
    private (double Min, double Max) VisibleListRange(Drag drag)
    {
        var panelTop = drag.PanelTopInExtent - EntriesScroller.Offset.Y;
        return (Math.Max(0, panelTop), Math.Min(EntriesScroller.Viewport.Height, panelTop + drag.Layout.TotalHeight));
    }

    private static void SlideRows(Drag drag)
    {
        for (var i = 0; i < drag.Rows.Length; i++)
        {
            var shift = drag.Layout.ShiftOf(i, drag.From, drag.Target);
            if (shift != drag.Shifts[i])
            {
                drag.Shifts[i] = shift;
                drag.Rows[i].RenderTransform = Slide(shift);
            }
        }
    }

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

    /// <summary><c>null</c> unless every row has a container.</summary>
    private Control[]? RowContainers(int count)
    {
        var rows = new Control[count];
        for (var i = 0; i < count; i++)
        {
            if (EntriesList.ContainerFromIndex(i) is not { } row)
            {
                return null;
            }

            rows[i] = row;
        }

        return rows;
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

    private sealed class Drag(ModCollectionsViewModel vm, IPointer pointer, Control[] rows, ListReorderLayout layout, int from, TimeSpan duration)
    {
        public ModCollectionsViewModel Vm { get; } = vm;

        public IPointer Pointer { get; } = pointer;

        /// <summary>The rows' containers, in list order before the drag.</summary>
        public Control[] Rows { get; } = rows;

        public ListReorderLayout Layout { get; } = layout;

        public int From { get; } = from;

        public TimeSpan Duration { get; } = duration;

        /// <summary>Pointer distance below the dragged row's top.</summary>
        public double GrabOffset { get; init; }

        /// <summary>The items panel's top in the scrolled content.</summary>
        public double PanelTopInExtent { get; init; }

        public TopLevel? KeyScope { get; init; }

        public int Target { get; set; } = from;

        public double[] Shifts { get; } = new double[rows.Length];

        public bool IsSettling { get; set; }

        public IDisposable? Settled { get; set; }
    }
}
