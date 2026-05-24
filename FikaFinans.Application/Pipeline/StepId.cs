namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Typed identifier for a pipeline step (1-10). The ten well-known steps are
/// exposed as static fields for symbolic access; <see cref="From(int)"/>
/// validates arbitrary integer inputs. <c>default(StepId)</c> is invalid by
/// construction — accessing <see cref="AgentName"/> on the default value
/// throws.
/// </summary>
public readonly record struct StepId
{
    public int Value { get; }

    private StepId(int value)
    {
        Value = value;
    }

    public string AgentName => Value switch
    {
        1  => "DataLoader",
        2  => "MetricsCalculator",
        3  => "MacroAnalyst",
        4  => "SignalScorer",
        5  => "MacroAligner",
        6  => "CatalystTagger",
        7  => "ThesisValidator",
        8  => "Recommender",
        9  => "UniverseEnricher",
        10 => "PortfolioConstructor",
        _  => throw new InvalidOperationException(
            $"StepId {Value} is invalid (steps are 1-10). Use From(int) or a static field."),
    };

    public static StepId From(int value)
    {
        if (value < 1 || value > 10)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Step id must be between 1 and 10.");
        return new StepId(value);
    }

    public override string ToString() => $"Step{Value:00}.{AgentName}";

    public static readonly StepId DataLoader           = new(1);
    public static readonly StepId MetricsCalculator    = new(2);
    public static readonly StepId MacroAnalyst         = new(3);
    public static readonly StepId SignalScorer         = new(4);
    public static readonly StepId MacroAligner         = new(5);
    public static readonly StepId CatalystTagger       = new(6);
    public static readonly StepId ThesisValidator      = new(7);
    public static readonly StepId Recommender          = new(8);
    public static readonly StepId UniverseEnricher     = new(9);
    public static readonly StepId PortfolioConstructor = new(10);

    public static readonly IReadOnlyList<StepId> All =
    [
        DataLoader, MetricsCalculator, MacroAnalyst, SignalScorer, MacroAligner,
        CatalystTagger, ThesisValidator, Recommender, UniverseEnricher, PortfolioConstructor,
    ];
}
