using System.Globalization;
using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;
using FikaFinans.InfrastructureV2.Tests.Models.Recommender;

namespace FikaFinans.InfrastructureV2.Tests.Agents.PortfolioConstructor;

public sealed class PortfolioConstructorAgent
{
    private static readonly Recommendation[] BuyRecs =
    [
        Recommendation.CatalystEntry,
        Recommendation.MomentumEntry,
    ];

    private static readonly Recommendation[] SellRecs =
    [
        Recommendation.ThesisExit,
        Recommendation.MomentumExit,
    ];

    private readonly PortfolioConstructorConfig _config;

    public PortfolioConstructorAgent() : this(PortfolioConstructorConfig.Default) { }

    public PortfolioConstructorAgent(PortfolioConstructorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var version = config.Meta?.ConfigVersion ?? PortfolioConstructorConfig.ExpectedConfigVersion;
        if (!string.Equals(version, PortfolioConstructorConfig.ExpectedConfigVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"config-10-portfolio.json config_version '{version}' does not match expected " +
                $"'{PortfolioConstructorConfig.ExpectedConfigVersion}'.");
        }
        _config = config;
    }

    public TradesOutput Run(string isoWeek, string runId, string? macroRegime = null)
    {
        var inputPath = Paths.UniverseEnricherOutput(isoWeek, runId);
        var input = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(inputPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 09 output at {inputPath}");

        var output = RunInMemory(input, macroRegime);
        WriteJson(Paths.PortfolioConstructorOutput(isoWeek, runId), output);
        return output;
    }

    public TradesOutput RunInMemory(DataLoaderOutput enriched, string? macroRegime = null)
    {
        ArgumentNullException.ThrowIfNull(enriched);

        // Pass A: capital math + integrity check on held positions.
        ValidateHeldPositions(enriched.Funds);
        var activeValue   = enriched.Funds.Where(f => f.CurrentlyHeld).Sum(f => f.CurrentValueKr ?? 0m);
        var frozenValue   = enriched.FrozenPositions.Sum(p => p.CurrentValueKr);
        var cashAvailable = enriched.CashAvailableKr;
        var portfolioVal  = activeValue + frozenValue + cashAvailable;
        var (floorPct, regimeOverrideActive) = ResolveCashFloor(macroRegime);
        var floorKr       = portfolioVal * floorPct;
        var aboveFloor    = Math.Max(0m, cashAvailable - floorKr);

        var trades     = new List<Trade>();
        var rejected   = new List<RejectedRecommendation>();
        var violations = new List<ConstraintViolation>();

        // Pass B + C + D: per-fund layer gate, conviction gate, recommendation
        // → trade conversion. Filtered Buys/Sells return null and live in
        // rejected[] only.
        foreach (var fund in enriched.Funds)
        {
            var trade = ProcessFund(fund, portfolioVal, rejected, violations);
            if (trade is not null) trades.Add(trade);
        }

        // Pass E: total deployable now that sells are sized.
        var sellProceeds = trades
            .Where(t => t.TradeType is TradeType.Sell or TradeType.PartialSell)
            .Sum(t => t.AmountKr);
        var totalDeployable = aboveFloor + sellProceeds;

        // Pass F: size buys (default + scale + min-trade dropout, capped at
        // max_position_pct × portfolio so we don't size a fresh Buy past the
        // concentration cap and immediately Trim it back).
        SizeBuys(trades, totalDeployable, portfolioVal, enriched.Funds, rejected, violations);

        // Pass G: concentration cap — emit Trim or suppress for core funds.
        EnforceConcentrationCap(trades, enriched.Funds, portfolioVal, violations);

        // Pass H: sector cap — refuse new Buys that would breach, otherwise
        // Trim largest non-core member.
        EnforceSectorCap(trades, enriched.Funds, portfolioVal, rejected, violations);

        // Pass I: rotation pair partial (one leg dropped).
        CheckRotationPairs(trades, enriched.Funds, rejected, violations);

        // Pass J: capital summary.
        var totalBuy      = trades.Where(t => t.TradeType is TradeType.Buy or TradeType.TopUp).Sum(t => t.AmountKr);
        var trimProceeds  = trades.Where(t => t.TradeType is TradeType.Trim).Sum(t => t.AmountKr);
        var realisedSells = trades.Where(t => t.TradeType is TradeType.Sell or TradeType.PartialSell).Sum(t => t.AmountKr);
        var cashRemaining = cashAvailable + realisedSells + trimProceeds - totalBuy;

        // Final defensive sanity check — this should never trigger after sizing.
        if (cashRemaining < 0m)
        {
            violations.Add(new ConstraintViolation
            {
                ViolationType = "cash_remaining_kr_negative",
                Isin          = null,
                Value         = cashRemaining.ToString("F2", CultureInfo.InvariantCulture),
                Message       = "cash_remaining_kr is negative — sizing did not converge; possible code bug.",
            });
        }

        // Cash-floor unmet defense (only after dropping all buys).
        if (totalBuy == 0m && trades.Any(t => t.TradeType is TradeType.Buy or TradeType.TopUp) is false &&
            cashAvailable + realisedSells + trimProceeds < floorKr)
        {
            violations.Add(new ConstraintViolation
            {
                ViolationType = "cash_floor_unmet",
                Isin          = null,
                Value         = floorKr.ToString("F2", CultureInfo.InvariantCulture),
                Message       = "Total cash + sell proceeds insufficient to maintain cash floor.",
            });
        }

        var summary = new CapitalSummary
        {
            PortfolioValueKr       = portfolioVal,
            ActivePositionsValueKr = activeValue,
            FrozenPositionsValueKr = frozenValue,
            CashAvailableKr        = cashAvailable,
            SellProceedsKr         = realisedSells,
            TotalDeployableKr      = totalDeployable,
            TotalBuyAmountKr       = totalBuy,
            CashRemainingKr        = cashRemaining,
            CashFloorKr            = floorKr,
            CashAboveFloorKr       = aboveFloor,
            CashPolicy             = new CashPolicySummary
            {
                FloorPct             = floorPct,
                RegimeOverrideActive = regimeOverrideActive,
                RegimeUsed           = macroRegime,
            },
        };

        return new TradesOutput
        {
            GeneratedAt             = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek                 = enriched.IsoWeek,
            ConfigVersion           = PortfolioConstructorConfig.ExpectedConfigVersion,
            Trades                  = trades,
            RejectedRecommendations = rejected,
            CapitalSummary          = summary,
            ConstraintViolations    = violations,
        };
    }

    // ----- Helpers -----

    private static void ValidateHeldPositions(IReadOnlyList<FundRecord> funds)
    {
        foreach (var f in funds.Where(f => f.CurrentlyHeld))
        {
            if (f.CurrentValueKr is null || f.CurrentValueKr.Value < 0m)
            {
                throw new InvalidDataException(
                    $"Held fund {f.Isin} has invalid current_value_kr={f.CurrentValueKr?.ToString("F2", CultureInfo.InvariantCulture) ?? "null"}.");
            }
        }
    }

    private (decimal floorPct, bool regimeOverrideActive) ResolveCashFloor(string? macroRegime)
    {
        if (!_config.CashPolicy.MacroOverrideEnabled || string.IsNullOrEmpty(macroRegime))
        {
            return (_config.CashPolicy.FloorPct, false);
        }
        if (_config.CashPolicy.MacroOverrideTable.TryGetValue(macroRegime, out var pct))
        {
            return (pct, pct != _config.CashPolicy.FloorPct);
        }
        return (_config.CashPolicy.FloorPct, false);
    }

    private Trade? ProcessFund(
        FundRecord fund,
        decimal portfolioValue,
        List<RejectedRecommendation> rejected,
        List<ConstraintViolation> violations)
    {
        var rec        = fund.Recommendation ?? Recommendation.Skip;
        var conviction = fund.ConvictionScore ?? 0m;
        var thesis     = fund.ThesisValidity ?? Models.ThesisValidator.ThesisValidity.NotApplicable;

        // Layer gate (runs BEFORE conviction gate — see contract). Core funds
        // can't sell/partial-sell. The Trim suppression for core funds is
        // applied later in the concentration/sector passes.
        if (fund.Layer == FundLayer.Core && SellRecs.Contains(rec))
        {
            rejected.Add(new RejectedRecommendation
            {
                Isin                 = fund.Isin,
                SourceRecommendation = rec,
                RejectedBecause      = "core_locked",
                WouldHaveBeen        = WouldHaveBeenSell(rec, fund),
            });
            violations.Add(new ConstraintViolation
            {
                ViolationType = "core_locked_recommendation_suppressed",
                Isin          = fund.Isin,
                Value         = rec.ToString(),
                Message       = $"Core fund {fund.Isin} {rec} suppressed; emitting Hold.",
            });
            return MakeTrade(fund,
                fund.CurrentlyHeld ? TradeType.Hold : TradeType.NoOp,
                "core_locked", 0m, conviction);
        }

        // Conviction gate: Sells.
        if (SellRecs.Contains(rec))
        {
            var thesisInvalidOverride =
                _config.Guards.SkipSellBelowConvictionUnlessThesisInvalid &&
                thesis == Models.ThesisValidator.ThesisValidity.Invalid;
            if (conviction < _config.Guards.SkipSellBelowConviction && !thesisInvalidOverride)
            {
                rejected.Add(new RejectedRecommendation
                {
                    Isin                 = fund.Isin,
                    SourceRecommendation = rec,
                    RejectedBecause      = $"conviction_below_floor (conviction {conviction.ToString("F2", CultureInfo.InvariantCulture)} < {_config.Guards.SkipSellBelowConviction.ToString("F2", CultureInfo.InvariantCulture)})",
                    WouldHaveBeen        = WouldHaveBeenSell(rec, fund),
                });
                if (conviction >= 0.8m)
                {
                    violations.Add(new ConstraintViolation
                    {
                        ViolationType = "conviction_gate_unusual",
                        Isin          = fund.Isin,
                        Value         = conviction.ToString("F2", CultureInfo.InvariantCulture),
                        Message       = $"High-conviction Sell on {fund.Isin} ({conviction.ToString("F2", CultureInfo.InvariantCulture)}) was filtered; thesis was {thesis}.",
                    });
                }
                return null;
            }
        }

        // Conviction gate: Buys.
        if (BuyRecs.Contains(rec) && conviction < _config.Guards.SkipBuyBelowConviction)
        {
            rejected.Add(new RejectedRecommendation
            {
                Isin                 = fund.Isin,
                SourceRecommendation = rec,
                RejectedBecause      = $"conviction_below_floor (conviction {conviction.ToString("F2", CultureInfo.InvariantCulture)} < {_config.Guards.SkipBuyBelowConviction.ToString("F2", CultureInfo.InvariantCulture)})",
                WouldHaveBeen        = $"{rec} (size pending)",
            });
            return null;
        }

        // Conversion table — see 10-portfolioconstructor.md, "Recommendation → Trade conversion".
        return rec switch
        {
            Recommendation.CatalystEntry =>
                fund.CurrentlyHeld
                    ? (BelowTarget(fund, portfolioValue)
                        ? MakeTrade(fund, TradeType.TopUp, "catalyst_entry_topup", 0m, conviction)
                        : MakeTrade(fund, TradeType.Hold,  "catalyst_entry_at_target", 0m, conviction))
                    : MakeTrade(fund, TradeType.Buy, "catalyst_entry_fresh", 0m, conviction),

            Recommendation.MomentumEntry =>
                fund.CurrentlyHeld
                    ? (BelowTarget(fund, portfolioValue)
                        ? MakeTrade(fund, TradeType.TopUp, "momentum_entry_topup", 0m, conviction)
                        : MakeTrade(fund, TradeType.Hold,  "momentum_entry_at_target", 0m, conviction))
                    : MakeTrade(fund, TradeType.Buy, "momentum_entry_fresh", 0m, conviction),

            Recommendation.ThesisExit when fund.CurrentlyHeld && (fund.CurrentValueKr ?? 0m) > 0m =>
                MakeTrade(fund, TradeType.Sell, "thesis_exit_full", fund.CurrentValueKr!.Value, conviction),
            Recommendation.ThesisExit when fund.CurrentlyHeld =>
                MakeNoOp(fund, "thesis_exit_zero_value", conviction, "would have been Sell but current_value_kr is 0"),
            Recommendation.ThesisExit =>
                MakeNoOp(fund, "thesis_exit_not_held", conviction, "would have been Sell but not held"),

            Recommendation.MomentumExit when fund.CurrentlyHeld && (fund.CurrentValueKr ?? 0m) > 0m =>
                conviction >= 0.7m
                    ? MakeTrade(fund, TradeType.Sell, "momentum_exit_full_high_conviction",
                                fund.CurrentValueKr!.Value, conviction)
                    : MakeTrade(fund, TradeType.PartialSell, "momentum_exit_decay",
                                Math.Round(fund.CurrentValueKr!.Value * 0.5m, 2, MidpointRounding.AwayFromZero),
                                conviction),
            Recommendation.MomentumExit when fund.CurrentlyHeld =>
                MakeNoOp(fund, "momentum_exit_zero_value", conviction, "would have been PartialSell but current_value_kr is 0"),
            Recommendation.MomentumExit =>
                MakeNoOp(fund, "momentum_exit_not_held", conviction, "would have been PartialSell/Sell but not held"),

            Recommendation.Maintain when fund.CurrentlyHeld =>
                MakeTrade(fund, TradeType.Hold, "maintain_default", 0m, conviction),
            Recommendation.Maintain =>
                MakeNoOp(fund, "maintain_no_position", conviction, "Maintain on a non-held fund (Recommender inconsistency)"),

            Recommendation.Skip when fund.CurrentlyHeld =>
                MakeTrade(fund, TradeType.Hold, "skip_held", 0m, conviction),
            Recommendation.Skip =>
                MakeTrade(fund, TradeType.NoOp, "skip_default", 0m, conviction),

            _ => MakeTrade(fund, TradeType.NoOp, "no_action", 0m, conviction),
        };
    }

    private bool BelowTarget(FundRecord fund, decimal portfolioValue)
    {
        if (portfolioValue <= 0m) return true;
        var currentWeight = (fund.CurrentValueKr ?? 0m) / portfolioValue;
        return currentWeight < _config.DefaultBuyTargetPct;
    }

    private static string WouldHaveBeenSell(Recommendation rec, FundRecord fund)
    {
        var amount = fund.CurrentValueKr ?? 0m;
        var formatted = amount.ToString("F0", CultureInfo.InvariantCulture);
        return rec switch
        {
            Recommendation.ThesisExit   => $"Sell {formatted} kr",
            Recommendation.MomentumExit => $"PartialSell ~{(amount * 0.5m).ToString("F0", CultureInfo.InvariantCulture)} kr",
            _                           => rec.ToString(),
        };
    }

    private static Trade MakeTrade(
        FundRecord fund,
        TradeType trade,
        string reason,
        decimal amountKr,
        decimal conviction,
        IReadOnlyList<string>? auditNotes = null) => new()
    {
        Isin                 = fund.Isin,
        FundName             = fund.Metadata.Name,
        TradeType            = trade,
        TradeReason          = reason,
        AmountKr             = amountKr,
        SourceRecommendation = fund.Recommendation ?? Recommendation.Skip,
        SourceConviction     = conviction,
        RotationPairId       = fund.RotationPairId,
        TrimReason           = trade == TradeType.Trim ? reason : null,
        ScalingFactor        = 1.0m,
        AuditNotes           = auditNotes ?? Array.Empty<string>(),
    };

    private static Trade MakeNoOp(FundRecord fund, string reason, decimal conviction, string note) =>
        MakeTrade(fund, TradeType.NoOp, reason, 0m, conviction, [note]);

    private static Trade WithAmountAndScaling(Trade t, decimal amount, decimal scaling) => new()
    {
        Isin                 = t.Isin,
        FundName             = t.FundName,
        TradeType            = t.TradeType,
        TradeReason          = t.TradeReason,
        AmountKr             = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
        SourceRecommendation = t.SourceRecommendation,
        SourceConviction     = t.SourceConviction,
        RotationPairId       = t.RotationPairId,
        TrimReason           = t.TrimReason,
        ScalingFactor        = scaling,
        AuditNotes           = t.AuditNotes,
    };

    // Default per-buy target = max(default_buy_target_pct, 1/N) × deployable,
    // capped at max_position_pct × portfolio - current_value (concentration
    // headroom). Drop sub-min-trade buys; redistribute their freed cash among
    // survivors (still respecting headroom).
    private void SizeBuys(
        List<Trade> trades,
        decimal totalDeployable,
        decimal portfolioValue,
        IReadOnlyList<FundRecord> funds,
        List<RejectedRecommendation> rejected,
        List<ConstraintViolation> violations)
    {
        var buys = trades
            .Where(t => t.TradeType is TradeType.Buy or TradeType.TopUp)
            .Select(t => t.Isin)
            .ToList();
        if (buys.Count == 0) return;

        if (totalDeployable <= 0m)
        {
            foreach (var isin in buys)
            {
                var idx = trades.FindIndex(t => string.Equals(t.Isin, isin, StringComparison.Ordinal));
                if (idx < 0) continue;
                var t = trades[idx];
                rejected.Add(new RejectedRecommendation
                {
                    Isin                 = isin,
                    SourceRecommendation = t.SourceRecommendation,
                    RejectedBecause      = "no_deployable_cash",
                    WouldHaveBeen        = $"{t.TradeType}",
                });
                trades.RemoveAt(idx);
            }
            return;
        }

        var maxPositionValue = _config.Constraints.MaxPositionPctOfPortfolio * portfolioValue;
        var headroomByIsin = buys.ToDictionary(
            isin => isin,
            isin =>
            {
                var f = funds.First(x => string.Equals(x.Isin, isin, StringComparison.Ordinal));
                var current = f.CurrentValueKr ?? 0m;
                return Math.Max(0m, maxPositionValue - current);
            },
            StringComparer.Ordinal);

        var perBuyPct = Math.Max(_config.DefaultBuyTargetPct, 1.0m / buys.Count);
        var requested = perBuyPct * totalDeployable;
        var totalReq  = requested * buys.Count;
        var scaling   = totalReq > totalDeployable ? totalDeployable / totalReq : 1.0m;

        var amounts = buys.ToDictionary(
            isin => isin,
            isin => Math.Min(requested * scaling, headroomByIsin[isin]),
            StringComparer.Ordinal);

        if (_config.Sizing.DropBuyIfFallsBelowMinTrade)
        {
            var minTrade = _config.Constraints.MinTradeKr;
            while (true)
            {
                var smallest = amounts
                    .Where(kv => kv.Value < minTrade)
                    .OrderBy(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => (Isin: kv.Key, Amount: kv.Value))
                    .FirstOrDefault();
                if (smallest.Isin is null) break;

                var dropIdx = trades.FindIndex(t => string.Equals(t.Isin, smallest.Isin, StringComparison.Ordinal));
                var dropped = trades[dropIdx];
                var capped  = headroomByIsin[smallest.Isin] < requested * scaling;
                var because = capped
                    ? $"min_trade_dropout (capped at {smallest.Amount.ToString("F0", CultureInfo.InvariantCulture)} kr by concentration headroom; below min_trade_kr {minTrade.ToString("F0", CultureInfo.InvariantCulture)})"
                    : $"min_trade_dropout (scaled to {smallest.Amount.ToString("F0", CultureInfo.InvariantCulture)} kr < {minTrade.ToString("F0", CultureInfo.InvariantCulture)} kr)";
                rejected.Add(new RejectedRecommendation
                {
                    Isin                 = dropped.Isin,
                    SourceRecommendation = dropped.SourceRecommendation,
                    RejectedBecause      = because,
                    WouldHaveBeen        = $"{dropped.TradeType} {smallest.Amount.ToString("F0", CultureInfo.InvariantCulture)} kr",
                });
                violations.Add(new ConstraintViolation
                {
                    ViolationType = "min_trade_dropout",
                    Isin          = dropped.Isin,
                    Value         = smallest.Amount.ToString("F2", CultureInfo.InvariantCulture),
                    Message       = $"{dropped.Isin} dropped — final amount {smallest.Amount.ToString("F0", CultureInfo.InvariantCulture)} kr below min_trade_kr.",
                });
                trades.RemoveAt(dropIdx);
                amounts.Remove(smallest.Isin);
                headroomByIsin.Remove(smallest.Isin);

                if (amounts.Count == 0) break;
                var bonus = smallest.Amount / amounts.Count;
                foreach (var key in amounts.Keys.ToList())
                    amounts[key] = Math.Min(amounts[key] + bonus, headroomByIsin[key]);
            }
        }

        for (var i = 0; i < trades.Count; i++)
        {
            if (trades[i].TradeType is TradeType.Buy or TradeType.TopUp &&
                amounts.TryGetValue(trades[i].Isin, out var amt))
            {
                trades[i] = WithAmountAndScaling(trades[i], amt, scaling);
            }
        }
    }

    private void EnforceConcentrationCap(
        List<Trade> trades,
        IReadOnlyList<FundRecord> funds,
        decimal portfolioValue,
        List<ConstraintViolation> violations)
    {
        if (portfolioValue <= 0m) return;

        var cap      = _config.Constraints.MaxPositionPctOfPortfolio;
        var capValue = portfolioValue * cap;

        for (var i = 0; i < trades.Count; i++)
        {
            var trade = trades[i];
            var fund  = funds.FirstOrDefault(f => string.Equals(f.Isin, trade.Isin, StringComparison.Ordinal));
            if (fund is null || !fund.CurrentlyHeld) continue;
            if (trade.TradeType is TradeType.Sell or TradeType.PartialSell or TradeType.Trim) continue;

            var current  = fund.CurrentValueKr ?? 0m;
            var topup    = trade.TradeType is TradeType.Buy or TradeType.TopUp ? trade.AmountKr : 0m;
            var postValue = current + topup;
            if (postValue <= capValue) continue;

            if (fund.Layer == FundLayer.Core)
            {
                violations.Add(new ConstraintViolation
                {
                    ViolationType = "core_locked_recommendation_suppressed",
                    Isin          = fund.Isin,
                    Value         = postValue.ToString("F2", CultureInfo.InvariantCulture),
                    Message       = $"Core fund {fund.Isin} above {(cap * 100m).ToString("F0", CultureInfo.InvariantCulture)}% concentration cap; Trim suppressed.",
                });
                continue;
            }

            var trimAmount = Math.Round(postValue - capValue, 2, MidpointRounding.AwayFromZero);
            trades[i] = MakeTrade(fund, TradeType.Trim, "concentration_cap", trimAmount, trade.SourceConviction);
            violations.Add(new ConstraintViolation
            {
                ViolationType = "position_exceeds_concentration_cap",
                Isin          = fund.Isin,
                Value         = postValue.ToString("F2", CultureInfo.InvariantCulture),
                Message       = $"{fund.Isin} above {(cap * 100m).ToString("F0", CultureInfo.InvariantCulture)}% cap; Trim {trimAmount.ToString("F0", CultureInfo.InvariantCulture)} kr.",
            });
        }
    }

    private void EnforceSectorCap(
        List<Trade> trades,
        IReadOnlyList<FundRecord> funds,
        decimal portfolioValue,
        List<RejectedRecommendation> rejected,
        List<ConstraintViolation> violations)
    {
        if (portfolioValue <= 0m) return;

        var cap      = _config.Constraints.MaxSectorPctOfPortfolio;
        var capValue = portfolioValue * cap;

        // Recompute every iteration since refused Buys change the sector totals.
        while (true)
        {
            var post = ComputePostTradeValues(trades, funds);
            var breach = post
                .GroupBy(p => p.Category, StringComparer.Ordinal)
                .Where(g => g.Sum(x => x.Value) > capValue)
                .OrderByDescending(g => g.Sum(x => x.Value))
                .FirstOrDefault();
            if (breach is null) return;

            var members = breach.OrderByDescending(x => x.Value).ToList();
            var total   = members.Sum(x => x.Value);
            var excess  = total - capValue;

            // Step 1: refuse new Buys in this sector first (smallest first to
            // minimize collateral) — covers the "Refuses Buys that would breach"
            // contract clause.
            var newBuys = members
                .Select(m => (Member: m, Trade: trades.FirstOrDefault(t => string.Equals(t.Isin, m.Isin, StringComparison.Ordinal))))
                .Where(p => p.Trade?.TradeType is TradeType.Buy)
                .OrderBy(p => p.Trade!.AmountKr)
                .ToList();
            if (newBuys.Count > 0)
            {
                var buyToRefuse = newBuys.First();
                var bt          = buyToRefuse.Trade!;
                trades.Remove(bt);
                rejected.Add(new RejectedRecommendation
                {
                    Isin                 = bt.Isin,
                    SourceRecommendation = bt.SourceRecommendation,
                    RejectedBecause      = "sector_cap_breach",
                    WouldHaveBeen        = $"Buy {bt.AmountKr.ToString("F0", CultureInfo.InvariantCulture)} kr",
                });
                violations.Add(new ConstraintViolation
                {
                    ViolationType = "sector_exceeds_cap",
                    Isin          = bt.Isin,
                    Value         = total.ToString("F2", CultureInfo.InvariantCulture),
                    Message       = $"Sector '{breach.Key}' would exceed {(cap * 100m).ToString("F0", CultureInfo.InvariantCulture)}% cap; Buy {bt.Isin} refused.",
                });
                continue;
            }

            // Step 2: held positions themselves over cap → Trim largest non-core.
            var largest = members.FirstOrDefault(m => m.Layer != FundLayer.Core);
            if (largest is null)
            {
                violations.Add(new ConstraintViolation
                {
                    ViolationType = "core_locked_recommendation_suppressed",
                    Isin          = null,
                    Value         = breach.Key,
                    Message       = $"Sector '{breach.Key}' above {(cap * 100m).ToString("F0", CultureInfo.InvariantCulture)}% cap; all members core — Trim suppressed.",
                });
                return;
            }

            var fund       = funds.First(f => string.Equals(f.Isin, largest.Isin, StringComparison.Ordinal));
            var trimAmount = Math.Round(Math.Min(excess, largest.Value), 2, MidpointRounding.AwayFromZero);
            var conviction = fund.ConvictionScore ?? 0m;

            var idx  = trades.FindIndex(t => string.Equals(t.Isin, largest.Isin, StringComparison.Ordinal));
            var trim = MakeTrade(fund, TradeType.Trim, "sector_cap", trimAmount, conviction);
            if (idx >= 0) trades[idx] = trim;
            else           trades.Add(trim);

            violations.Add(new ConstraintViolation
            {
                ViolationType = "sector_exceeds_cap",
                Isin          = largest.Isin,
                Value         = total.ToString("F2", CultureInfo.InvariantCulture),
                Message       = $"Sector '{breach.Key}' at {total.ToString("F0", CultureInfo.InvariantCulture)} kr above {(cap * 100m).ToString("F0", CultureInfo.InvariantCulture)}% cap; trimming {largest.Isin} by {trimAmount.ToString("F0", CultureInfo.InvariantCulture)} kr.",
            });
            return;
        }
    }

    private sealed record PostTradePosition(string Isin, string Category, decimal Value, FundLayer Layer);

    private static List<PostTradePosition> ComputePostTradeValues(
        List<Trade> trades, IReadOnlyList<FundRecord> funds)
    {
        var result = new List<PostTradePosition>();
        foreach (var fund in funds)
        {
            var trade   = trades.FirstOrDefault(t => string.Equals(t.Isin, fund.Isin, StringComparison.Ordinal));
            var current = fund.CurrentValueKr ?? 0m;
            var post    = current;
            if (trade is not null)
            {
                post = trade.TradeType switch
                {
                    TradeType.Buy         => trade.AmountKr,
                    TradeType.TopUp       => current + trade.AmountKr,
                    TradeType.Sell        => 0m,
                    TradeType.PartialSell => current - trade.AmountKr,
                    TradeType.Trim        => current - trade.AmountKr,
                    _                     => current,
                };
            }
            if (post > 0m)
                result.Add(new PostTradePosition(fund.Isin, fund.Metadata.Category, post, fund.Layer));
        }
        return result;
    }

    private void CheckRotationPairs(
        List<Trade> trades,
        IReadOnlyList<FundRecord> funds,
        List<RejectedRecommendation> rejected,
        List<ConstraintViolation> violations)
    {
        var executedPairs = trades
            .Where(t => !string.IsNullOrEmpty(t.RotationPairId))
            .GroupBy(t => t.RotationPairId!, StringComparer.Ordinal);

        foreach (var pair in executedPairs)
        {
            var hasBuy  = pair.Any(t => t.TradeType is TradeType.Buy or TradeType.TopUp);
            var hasSell = pair.Any(t => t.TradeType is TradeType.Sell or TradeType.PartialSell);

            if (hasSell && !hasBuy)
            {
                var rejectedBuy = rejected.FirstOrDefault(r =>
                    BuyRecs.Contains(r.SourceRecommendation) &&
                    string.Equals(
                        funds.FirstOrDefault(f => string.Equals(f.Isin, r.Isin, StringComparison.Ordinal))?.RotationPairId,
                        pair.Key,
                        StringComparison.Ordinal));
                if (rejectedBuy is not null)
                {
                    violations.Add(new ConstraintViolation
                    {
                        ViolationType = "rotation_pair_partial",
                        Isin          = rejectedBuy.Isin,
                        Value         = pair.Key,
                        Message       = $"Rotation pair {pair.Key}: Buy leg {rejectedBuy.Isin} rejected ({rejectedBuy.RejectedBecause}); Sell executed alone.",
                    });
                }
            }
        }
    }

    private static void WriteJson(string path, TradesOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
