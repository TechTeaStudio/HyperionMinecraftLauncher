using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Trims the dash-less Minecraft UUID to a short prefix for the account-switcher row.
/// E.g. <c>"069a79f444e94726a5befca90e38aaf5"</c> -> <c>"069a79f4..."</c>.
/// </summary>
public sealed class UuidToShort : IValueConverter
{
    public static readonly UuidToShort Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string uuid || string.IsNullOrEmpty(uuid)) return string.Empty;
        return uuid.Length <= 8 ? uuid : uuid[..8] + "...";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// MultiBinding converter for the active-account checkmark: returns true when the row's
/// account id matches the active account's id. Inputs are <c>[rowId, activeAccount]</c>.
/// </summary>
public sealed class ActiveAccountMatchMulti : IMultiValueConverter
{
    public static readonly ActiveAccountMatchMulti Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return false;
        var rowId = values[0] as string;
        var active = values[1] as Account;
        if (rowId is null || active is null) return false;
        return string.Equals(rowId, active.Id, StringComparison.Ordinal);
    }
}
