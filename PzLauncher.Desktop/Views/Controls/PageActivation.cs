using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PzLauncher.Desktop.Views.Controls;

/// <summary>Runs a page's activation command each time the page loads; the run-once rule stays in the ViewModel.</summary>
public static class PageActivation
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(PageActivation));

    static PageActivation()
    {
        CommandProperty.Changed.AddClassHandler<Control, ICommand?>(OnCommandChanged);
    }

    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs<ICommand?> e)
    {
        control.Loaded -= OnLoaded;
        if (e.NewValue.GetValueOrDefault() is not null)
        {
            control.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && GetCommand(control) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
