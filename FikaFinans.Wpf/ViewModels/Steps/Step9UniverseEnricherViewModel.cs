using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Windows.Input;
using System.Windows.Media;
using DevExpress.Mvvm;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using FikaFinans.Wpf.Views.Charts;
using JetBrains.Annotations;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NLog;
using SkiaSharp;
using System.Text.Json;

namespace FikaFinans.Wpf.ViewModels.Steps;

/// <summary>
/// The step 9 tab: shows the last universe pass, following the step's events rather than
/// driving them, with a debug button that runs a pass again without storing it.
/// </summary>
public sealed class Step9UniverseEnricherViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IStep09UniverseEnricher? _step09;
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

    [UsedImplicitly]
    public Step9UniverseEnricherViewModel()   // designer only
    {
        FundClickedCommand = new DelegateCommand<string>(OnFundClicked);
    }

    [UsedImplicitly]
    public Step9UniverseEnricherViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IStep09UniverseEnricher step09,
        IConfigEditorDialogService configEditor,
        IFundDetailDialogService fundDetailDialog,
        IStepEventSource stepEvents)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _step09 = step09;
        _configEditorService = configEditor;
        _fundDetailDialog = fundDetailDialog;
        FundClickedCommand = new DelegateCommand<string>(OnFundClicked);

        // The tab follows the step rather than running it: whoever drove the pass — step 8's
        // done-signal or the daily timer locally, a Function in Azure — reports through this
        // stream.
        var ticks = stepEvents.Events
            .Where(tick => tick.Step == StepId.UniverseEnricher)
            .ObserveOn(uiScheduler);

        Disposables.Add(ticks.Subscribe(ApplyStepEvent));

        // No done-signal follows step 9, so the pass's own Succeeded is the cue to reload.
        Disposables.Add(ticks
            .Where(tick => tick.Kind == StepEventKind.Succeeded)
            .Select(_ => Observable
                .FromAsync(LoadOutputAsync)
                // Caught per load: an error reaching Subscribe would end the stream for the
                // session, and the next pass's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 9 output load failed");
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    private void OnFundClicked(string isin)
    {
        _fundDetailDialog?.Show(isin, IsoWeek, RunId,
            System.Windows.Application.Current.MainWindow);
    }

    protected override string? GetConfigPath() => _paths?.Config09ConvictionJson;

    /// <summary>
    /// The debug button: a pass over the universe as it stands, through the step, shown but
    /// never stored — a replaced output would change what step 10 reads. Runs whether or not
    /// the barrier has closed. Not free: one LLM call per Buy with peers.
    /// </summary>
    protected override async Task RunStepCoreAsync()
    {
        // Null only under the designer's parameterless constructor.
        if (_step09 is null) return;

        var output = await _step09.ComputeOutputAsync();

        // The funds only, the same shape a stored output reads back as.
        OutputJson = JsonSerializer.Serialize(output.Funds, JsonOptions.Default);
        BuildSignalsChart(output.Funds);
        OutputSummaryText = $"Debug run, not stored — {output.Funds.Count} funds — week {output.IsoWeek}";
    }

    /// <summary>
    /// Reads the last pass back off the rows — the universe's <c>Step09Json</c>, not the run
    /// bar's run: each row keeps its own fund's run id, so no single run id gathers a pass.
    /// </summary>
    public override async Task LoadOutputAsync()
    {
        if (_step09 is null) return;

        var output = await _step09.ReadOutputAsync();

        if (output is null)
        {
            OutputSummaryText = "No universe pass stored yet";
            return;
        }

        OutputJson = output.Json;
        BuildSignalsChart(output.Funds);
        OutputSummaryText = $"{output.Funds.Count} funds — universe enriched";
    }

    private void BuildSignalsChart(IReadOnlyList<FundRecord> funds)
    {
        var series = new List<ISeries>();
        var legendItems = new List<SignalsLegendItem>();
        var seenSignals = new HashSet<string>();

        foreach (var fund in funds)
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
