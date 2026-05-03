namespace FikaFinans.Infrastructure.Foundry;

/// <summary>
/// Foundry deployment names for the multi-model comparison. One adapter, one SDK path —
/// only this string changes per registration. Names are the auto-generated deployment
/// names from the Foundry portal (the new portal locks them at deploy time). See
/// <c>Docs/models.md</c> for the deployed lineup, the rejected candidates, and the
/// checklist for adding an N-th model later.
/// </summary>
public static class FoundryModelIds
{
    public const string Gpt5_4_1 = "gpt-5.4-1";
    public const string DeepSeekR1_0528_1 = "DeepSeek-R1-0528-1";

    public static IReadOnlyList<string> ComparisonModels { get; } = [Gpt5_4_1, DeepSeekR1_0528_1];
}
