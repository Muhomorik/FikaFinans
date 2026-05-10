using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Bank.Events;

public sealed record FundPositionDto(
    FundId FundId,
    string FundName,
    Isin Isin,
    decimal Units,
    Money CurrentValue,
    Money CostBasis,
    Money UnrealizedGainLoss,
    decimal GainLossPercent);
