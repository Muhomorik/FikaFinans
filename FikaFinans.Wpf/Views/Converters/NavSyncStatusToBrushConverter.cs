using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Wpf.Views.Converters;

/// <summary>
/// Maps a <see cref="NavSyncStatusKind"/> to a status brush for the NAV Sync
/// grid + legend. Colours mirror the theme's <c>ff.*</c> brushes so the
/// will-raise rows (New = blue, Changed = green) pull the eye while Up-to-date
/// (gray) and Processing (orange) recede. The same converter drives the legend,
/// so the two can never drift.
/// </summary>
public sealed class NavSyncStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NavSyncStatusKind kind) return DependencyProperty.UnsetValue;
        return kind switch
        {
            NavSyncStatusKind.New => new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB)),       // ff.InfoBrush
            NavSyncStatusKind.Changed => new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),   // ff.SuccessBrush
            NavSyncStatusKind.UpToDate => new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),  // ff.MutedBrush
            NavSyncStatusKind.Processing => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),// ff.WarningBrush
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
