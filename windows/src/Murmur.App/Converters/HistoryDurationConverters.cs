using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Murmur.App.Localization;

namespace Murmur.App.Converters;

/// <summary>Formats audio duration for history entries using localized UiText.</summary>
public sealed class HistoryAudioDurationConverter : IValueConverter
{
    /// <summary>Singleton instance of the converter.</summary>
    public static readonly HistoryAudioDurationConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return UiText.Format(nameof(UiText.HistoryAudioDuration), d);
        }
        return value?.ToString();
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats inference duration for history entries using localized UiText.</summary>
public sealed class HistoryInferenceDurationConverter : IValueConverter
{
    /// <summary>Singleton instance of the converter.</summary>
    public static readonly HistoryInferenceDurationConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return UiText.Format(nameof(UiText.HistoryInferenceDuration), d);
        }
        return value?.ToString();
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
