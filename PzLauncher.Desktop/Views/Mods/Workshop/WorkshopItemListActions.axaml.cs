using Avalonia;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Views.Mods.Workshop;

public partial class WorkshopItemListActions : UserControl
{
    public static readonly StyledProperty<object?> MoreActionsProperty = AvaloniaProperty.Register<WorkshopItemListActions, object?>(nameof(MoreActions));

    public WorkshopItemListActions()
    {
        InitializeComponent();
    }

    /// <summary>The section's own action buttons, after the shared ones.</summary>
    public object? MoreActions
    {
        get => GetValue(MoreActionsProperty);
        set => SetValue(MoreActionsProperty, value);
    }
}
