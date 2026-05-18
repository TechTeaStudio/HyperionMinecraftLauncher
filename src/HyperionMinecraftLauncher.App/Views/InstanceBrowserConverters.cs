using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

/// <summary>
/// Loads a thumbnail from an on-disk PNG path. The screenshot tiles are 160x100 -
/// <see cref="Bitmap.DecodeToWidth"/> resamples on decode so we never hold full-res
/// pixel data in memory just for a thumbnail.
/// </summary>
public sealed class PngPathToThumbnail : IValueConverter
{
    public static readonly PngPathToThumbnail Instance = new();
    private const int ThumbnailWidth = 160;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Bitmap.DecodeToWidth(fs, ThumbnailWidth);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Format a byte count as a readable size (B, KB, MB, GB). One decimal once it leaves KB.</summary>
public sealed class ByteSizeFormatter : IValueConverter
{
    public static readonly ByteSizeFormatter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        long bytes;
        try { bytes = System.Convert.ToInt64(value); }
        catch { return string.Empty; }

        if (bytes < 1024) return $"{bytes} B";
        double k = bytes / 1024.0;
        if (k < 1024) return $"{k:0.#} KB";
        double m = k / 1024.0;
        if (m < 1024) return $"{m:0.#} MB";
        double g = m / 1024.0;
        return $"{g:0.##} GB";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convert a <see cref="DateTimeOffset"/> (e.g. <c>CrashReport.GeneratedAt</c>) into a
/// short humanised "ago" label using the same vocabulary as <see cref="IsoToHumanizedAgo"/>.
/// </summary>
public sealed class DateTimeOffsetToHumanizedAgo : IValueConverter
{
    public static readonly DateTimeOffsetToHumanizedAgo Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset when) return string.Empty;
        var delta = DateTimeOffset.UtcNow - when;
        if (delta.TotalSeconds < 0) return when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hours ago";
        if (delta.TotalDays < 2) return "yesterday";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} days ago";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)} months ago";
        return $"{(int)(delta.TotalDays / 365)} years ago";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convert an ISO-8601 timestamp string (e.g. from <c>LevelDatInfo.LastPlayedIso</c>)
/// into "3 hours ago" / "yesterday" / "2 days ago". Returns "never" for null/empty.
/// </summary>
public sealed class IsoToHumanizedAgo : IValueConverter
{
    public static readonly IsoToHumanizedAgo Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iso || string.IsNullOrWhiteSpace(iso))
            return "never";
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            return "never";

        var delta = DateTimeOffset.UtcNow - when;
        if (delta.TotalSeconds < 0) return when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hours ago";
        if (delta.TotalDays < 2) return "yesterday";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} days ago";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)} months ago";
        return $"{(int)(delta.TotalDays / 365)} years ago";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
