using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Step 5 label assigned per fund. Names match the JSON contract
/// in <c>Prompts/fund_signals.prompt.md</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignalLabel
{
    BuySignal,
    CatalystEntry,
    Watch,
    Pass,
    SellSignal,
}

/// <summary>
/// Three-state validity pill required on every non-Pass row, plus
/// <see cref="NotApplicable"/> for <see cref="SignalLabel.Pass"/> rows.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThesisValidity
{
    Valid,
    Partial,
    Invalid,
    NotApplicable,
}

/// <summary>
/// How directly the fund captures the macro thesis. Drives the catalyst-override
/// gate (Direct only).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExposureType
{
    Direct,
    Indirect,
}

/// <summary>Match strength against the week's rotation targets.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlignmentLevel
{
    Strong,
    Moderate,
    None,
}

/// <summary>Step 6 row type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Sell,
    Buy,
    Hold,
}
