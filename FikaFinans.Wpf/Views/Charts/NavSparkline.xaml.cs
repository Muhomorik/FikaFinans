using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace FikaFinans.Wpf.Views.Charts;

public partial class NavSparkline : UserControl
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(IEnumerable<ISeries>), typeof(NavSparkline),
            new PropertyMetadata(null));

    public IEnumerable<ISeries>? Series
    {
        get => (IEnumerable<ISeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public NavSparkline()
    {
        InitializeComponent();
    }
}
