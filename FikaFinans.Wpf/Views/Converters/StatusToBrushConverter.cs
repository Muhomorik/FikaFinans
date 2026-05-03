using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FikaFinans.Application.Agents;

namespace FikaFinans.Wpf.Views.Converters;

/// <summary>
/// Maps <see cref="FoundryFileStatus"/> to a foreground brush so the upload strip
/// pulls the eye toward Stale/Missing rows.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FoundryFileStatus status) return DependencyProperty.UnsetValue;
        return status switch
        {
            FoundryFileStatus.Fresh => new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xDB)),
            FoundryFileStatus.Stale => new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x00)),
            FoundryFileStatus.NotUploaded => new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x00)),
            FoundryFileStatus.Missing => new SolidColorBrush(Color.FromRgb(0xC1, 0x39, 0x2B)),
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps <see cref="ModelRunStatus"/> to a foreground brush for the per-model header dot.</summary>
public sealed class RunStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ViewModels.ModelRunStatus status) return DependencyProperty.UnsetValue;
        return status switch
        {
            ViewModels.ModelRunStatus.Idle => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            ViewModels.ModelRunStatus.Running => new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xDB)),
            ViewModels.ModelRunStatus.Done => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
            ViewModels.ModelRunStatus.Error => new SolidColorBrush(Color.FromRgb(0xC1, 0x39, 0x2B)),
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// True → Visible, false → Collapsed. Pass <c>"Invert"</c> as <c>ConverterParameter</c>
/// to flip the mapping (true → Collapsed).
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var truthy = value is true;
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (truthy ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
