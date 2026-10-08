using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DriveManager.Core.Models;

namespace DriveManager.App.Converters;

/// <summary>
/// Maps a <see cref="HealthLevel"/> to a brush: blue for good health, red (escalating) for problems.
/// Used to color health text so the user can tell healthy vs. problem drives at a glance.
/// </summary>
public sealed class HealthLevelToBrushConverter : IValueConverter
{
    // Blue = good, warm/red = problems.
    private static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7));     // blue
    private static readonly IBrush Watch = new SolidColorBrush(Color.FromRgb(0xE3, 0xA0, 0x08));    // amber
    private static readonly IBrush Warning = new SolidColorBrush(Color.FromRgb(0xF0, 0x5A, 0x28));  // orange-red
    private static readonly IBrush Critical = new SolidColorBrush(Color.FromRgb(0xE5, 0x1A, 0x1A)); // red
    private static readonly IBrush Unknown = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));  // gray

    public static readonly HealthLevelToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            HealthLevel.Good => Good,
            HealthLevel.Watch => Watch,
            HealthLevel.Warning => Warning,
            HealthLevel.Critical => Critical,
            _ => Unknown,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
