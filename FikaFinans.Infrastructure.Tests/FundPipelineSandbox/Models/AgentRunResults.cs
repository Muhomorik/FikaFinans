namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Wraps the parsed Step 5 output with the raw response text + token usage
/// for diagnostics. <see cref="ExtractedJson"/> is what was deserialized into
/// <see cref="Run"/>; <see cref="RawResponseText"/> is the model's full output
/// before fence-stripping.
/// </summary>
public sealed record FundSignalsRunResult(
    FundSignalsRun Run,
    string RawResponseText,
    string ExtractedJson,
    string ModelId,
    int InputTokens,
    int OutputTokens,
    long ElapsedMs);

/// <summary>Step 6 run result, mirroring <see cref="FundSignalsRunResult"/>.</summary>
public sealed record ActionConsolidationRunResult(
    ActionConsolidationRun Run,
    string RawResponseText,
    string ExtractedJson,
    string ModelId,
    int InputTokens,
    int OutputTokens,
    long ElapsedMs);
