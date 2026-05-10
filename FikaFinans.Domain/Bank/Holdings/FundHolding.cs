using System.Diagnostics;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;

namespace FikaFinans.Domain.Bank.Holdings;

[DebuggerDisplay("{FundId}: {Units:N4} units @ avg {AverageCostPerUnit:N2}")]
public class FundHolding
{
    public FundHoldingId Id { get; private init; }
    public FundId FundId { get; private init; }
    public decimal Units { get; private set; }
    public decimal AverageCostPerUnit { get; private set; }
    public decimal TotalCostBasis { get; private set; }
    public string Currency { get; private init; } = "SEK";

    private FundHolding() { }

    public static FundHolding Create(FundId fundId, string currency = "SEK")
    {
        return new FundHolding
        {
            Id = FundHoldingId.NewId(),
            FundId = fundId,
            Units = 0,
            AverageCostPerUnit = 0,
            TotalCostBasis = 0,
            Currency = currency
        };
    }

    public Result AddUnits(decimal units, Money totalCost)
    {
        if (units <= 0)
            return Result.Fail("Units must be positive.");

        var newTotalCost = TotalCostBasis + totalCost.Amount;
        var newUnits = Units + units;
        AverageCostPerUnit = newTotalCost / newUnits;
        Units = newUnits;
        TotalCostBasis = newTotalCost;
        return Result.Ok();
    }

    public Result<Money> RemoveUnits(decimal units)
    {
        if (units <= 0)
            return Result.Fail<Money>("Units must be positive.");
        if (units > Units)
            return Result.Fail<Money>($"Insufficient units: requested {units:N4}, available {Units:N4}.");

        var costBasisOfSoldUnits = Money.SEK(units * AverageCostPerUnit);
        Units -= units;
        TotalCostBasis = Units * AverageCostPerUnit;
        return Result.Ok(costBasisOfSoldUnits);
    }

    public Money GetCurrentValue(decimal currentNavPerUnit) => new(Units * currentNavPerUnit, Currency);

    public Money GetUnrealizedGainLoss(decimal currentNavPerUnit)
        => new(Units * currentNavPerUnit - TotalCostBasis, Currency);
}
