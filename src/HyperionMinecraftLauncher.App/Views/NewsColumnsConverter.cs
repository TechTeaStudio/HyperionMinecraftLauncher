using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// News grid columns from the list width: two large cards per row from 900 px up, one below.
/// Capped at two so a maximized Full HD window shows about four articles, not a dense grid.
/// </summary>
public sealed class NewsColumnsConverter : IValueConverter
{
    public static readonly NewsColumnsConverter Instance = new();

    private const double TwoColumnMinWidth = 900;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width && width >= TwoColumnMinWidth ? 2 : 1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
