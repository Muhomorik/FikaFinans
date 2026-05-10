using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;

namespace FikaFinans.Wpf.Views.Charts;

/// <summary>Legend item for the signals chart swatches strip.</summary>
public sealed record SignalsLegendItem(string Label, System.Windows.Media.Brush Color);

public partial class SignalsChart : UserControl
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(IEnumerable<ISeries>), typeof(SignalsChart),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LegendItemsProperty =
        DependencyProperty.Register(nameof(LegendItems), typeof(IEnumerable<SignalsLegendItem>), typeof(SignalsChart),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FundClickedCommandProperty =
        DependencyProperty.Register(nameof(FundClickedCommand), typeof(ICommand), typeof(SignalsChart));

    public IEnumerable<ISeries>? Series
    {
        get => (IEnumerable<ISeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public IEnumerable<SignalsLegendItem>? LegendItems
    {
        get => (IEnumerable<SignalsLegendItem>?)GetValue(LegendItemsProperty);
        set => SetValue(LegendItemsProperty, value);
    }

    public ICommand? FundClickedCommand
    {
        get => (ICommand?)GetValue(FundClickedCommandProperty);
        set => SetValue(FundClickedCommandProperty, value);
    }

    public SignalsChart()
    {
        InitializeComponent();
        Chart.XAxes = new[] { new DateTimeAxis(TimeSpan.FromDays(7), d => d.ToString("MMM dd")) };
        Chart.YAxes = new[] { new Axis { Name = "NAV (rebased 100)" } };
        Chart.ChartPointPointerDown += OnChartPointPointerDown;
    }

    private void OnChartPointPointerDown(IChartView chart, ChartPoint? point)
    {
        if (point?.Context.Series.Tag is not string isin) return;
        if (FundClickedCommand?.CanExecute(isin) == true)
            FundClickedCommand.Execute(isin);
    }
}
