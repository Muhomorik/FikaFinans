using System.Collections.Generic;
using System.IO;
using System.Reactive.Concurrency;
using System.Windows.Input;
using System.Windows.Media;
using DevExpress.Mvvm;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using FikaFinans.Wpf.Views.Charts;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NLog;
using SkiaSharp;
using System.Text.Json;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step9UniverseEnricherViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IUniverseEnricherAgent? _agent;
    private readonly IFundDetailDialogService? _fundDetailDialog;

    private IEnumerable<ISeries>? _signalsSeries;
    private IEnumerable<SignalsLegendItem>? _signalsLegend;

    public override int StepNumber => 9;
    public override string AgentName => "Universe enricher";
    public override bool HasConfig => true;

    public IEnumerable<ISeries>? SignalsSeries
    {
        get => _signalsSeries;
        private set => SetProperty(ref _signalsSeries, value, nameof(SignalsSeries));
    }

    public IEnumerable<SignalsLegendItem>? SignalsLegend
    {
        get => _signalsLegend;
        private set => SetProperty(ref _signalsLegend, value, nameof(SignalsLegend));
    }

    public ICommand FundClickedCommand { get; }

    public Step9UniverseEnricherViewModel()
    {
        FundClickedCommand = new DelegateCommand<string>(OnFundClicked);
    }

    public Step9UniverseEnricherViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IUniverseEnricherAgent agent,
        IConfigEditorDialogService configEditor,
        IFundDetailDialogService fundDetailDialog)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _configEditorService = configEditor;
        _fundDetailDialog = fundDetailDialog;
        FundClickedCommand = new DelegateCommand<string>(OnFundClicked);
    }

    private void OnFundClicked(string isin)
    {
        _fundDetailDialog?.Show(isin, IsoWeek, RunId,
            System.Windows.Application.Current.MainWindow);
    }

    protected override string? GetConfigPath() => _paths?.Config09ConvictionJson;

    protected override async Task RunStepCoreAsync()
    {
        if (_agent is null || _paths is null)
        {
            OutputSummaryText = "Configure Foundry credentials in Settings → Models";
            return;
        }
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        var output = await _agent.RunAsync(IsoWeek, RunId);

        var outPath = _paths.UniverseEnricherOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        BuildSignalsChart(output);
        OutputSummaryText = $"{output.Funds.Count} funds — universe enriched";
    }

    private void BuildSignalsChart(DataLoaderOutput output)
    {
        var series = new List<ISeries>();
        var legendItems = new List<SignalsLegendItem>();
        var seenSignals = new HashSet<string>();

        foreach (var fund in output.Funds)
        {
            var navPoints = BuildNavPoints(fund);
            if (navPoints.Count == 0) continue;

            var (skColor, labelText) = GetSignalColor(fund.Signal);
            var paint = new SolidColorPaint(skColor) { StrokeThickness = 2 };

            series.Add(new LineSeries<DateTimePoint>
            {
                Tag = fund.Isin.Value,
                Name = fund.Metadata.Name,
                Values = navPoints,
                Stroke = paint,
                Fill = null,
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(skColor),
                GeometryStroke = null,
            });

            if (seenSignals.Add(labelText))
            {
                var wpfColor = Color.FromRgb(skColor.Red, skColor.Green, skColor.Blue);
                legendItems.Add(new SignalsLegendItem(labelText, new SolidColorBrush(wpfColor)));
            }
        }

        SignalsSeries = series;
        SignalsLegend = legendItems;
    }

    private static List<DateTimePoint> BuildNavPoints(FundRecord fund)
    {
        var buckets = fund.NavBuckets;
        if (buckets.Count == 0) return [];

        var baseNav = (double)buckets[0].FirstNav;
        if (baseNav == 0) return [];

        var points = new List<DateTimePoint>(buckets.Count);
        foreach (var bucket in buckets)
        {
            var dt = bucket.PeriodStart.ToDateTime(TimeOnly.MinValue);
            var rebased = (double)bucket.LastNav / baseNav * 100.0;
            points.Add(new DateTimePoint(dt, rebased));
        }
        return points;
    }

    private static (SKColor color, string label) GetSignalColor(SignalLabel? signal) => signal switch
    {
        SignalLabel.Strength => (new SKColor(0x1F, 0x8A, 0x3D), "Strength"),
        SignalLabel.Weakness => (new SKColor(0xC2, 0x36, 0x2A), "Weakness"),
        SignalLabel.Forming  => (new SKColor(0xE0, 0x8E, 0x1A), "Forming"),
        SignalLabel.Neutral  => (new SKColor(0x7A, 0x7A, 0x7A), "Neutral"),
        _                    => (new SKColor(0xC9, 0xCC, 0xD0), "Pre-analysis"),
    };
}
