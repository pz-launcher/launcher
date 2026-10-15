using Avalonia;
using Avalonia.Controls;
using FluentIcons.Common;

namespace PzLauncher.Desktop.Views.Controls;

/// <summary>Icon + label navigation entry: a sidebar item by default, a title-bar tab with the <c>Shell.TitleBarTab</c> theme.</summary>
public sealed class NavItem : RadioButton
{
    public static readonly StyledProperty<Icon> IconProperty = AvaloniaProperty.Register<NavItem, Icon>(nameof(Icon));

    public Icon Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
