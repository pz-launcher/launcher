using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PzLauncher.Desktop.Views.Controls;

/// <summary>Binds a selectable item to an enum value: true when the source equals the parameter.</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public static EnumMatchConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    // Only the item being checked writes back; the one losing its check leaves the source alone.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
