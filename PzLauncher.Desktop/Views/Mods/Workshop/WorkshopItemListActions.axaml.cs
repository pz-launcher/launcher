using Avalonia;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Views.Mods.Workshop;

public partial class WorkshopItemListActions : UserControl
{
    public static readonly StyledProperty<object?> MoreActionsProperty = AvaloniaProperty.Register<WorkshopItemListActions, object?>(nameof(MoreActions));

    public static readonly StyledProperty<bool> ShowsGenerateProperty =
        AvaloniaProperty.Register<WorkshopItemListActions, bool>(nameof(ShowsGenerate), defaultValue: true);

    public WorkshopItemListActions()
    {
        InitializeComponent();
    }

    /// <summary>The button creating a mod preset from the list shows.</summary>
    public bool ShowsGenerate
    {
        get => GetValue(ShowsGenerateProperty);
        set => SetValue(ShowsGenerateProperty, value);
    }

    /// <summary>The section's own action buttons, after the shared ones.</summary>
    public object? MoreActions
    {
        get => GetValue(MoreActionsProperty);
        set => SetValue(MoreActionsProperty, value);
    }
}
