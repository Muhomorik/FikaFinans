# Agent 10: PortfolioConstructor

> Convert per-fund recommendations into executable trades subject to cash policy, concentration constraints, and conviction gating. The only agent that knows about portfolio state.

## Execution type
⚙️ Code — **strictly no LLM**. Determinism is required for backtest reproducibility.

## Inputs

| Source | What for |
|---|---|
| `09-enrichment-{iso_week}-{run_id}.json` | All per-fund records with full context including conviction and rotation_pair_id |
| `positions.csv` | Currently held funds with values (already in DataLoader output, but PortfolioConstructor re-reads for current state) |
| `config-10-portfolio.json` | Cash policy, constraints, conviction guards, sizing rules |

### Input expectations
- Every fund record carries a `recommendation` (one of six types from step 08).
- Every fund record carries a `conviction_score` and `universe_rank` from step 09.
- `positions.csv` snapshot must be aligned with the iso_week (positions as-of the same date as `as_of_date`).

## Outputs

### Output file
Pattern: `10-trades-{iso_week}-{run_id}.json`

### Output schema

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "trades": [
    {
      "isin": "...",
      "fund_name": "...",
      "trade": "Buy" | "TopUp" | "Trim" | "Sell" | "PartialSell" | "Hold" | "NoOp",
      "trade_reason": "string (structured tag)",
      "amount_kr": number,
      "source_recommendation": "CatalystEntry" | "MomentumEntry" | "ThesisExit" | "MomentumExit" | "Maintain" | "Skip",
      "source_conviction": 0.0,
      "rotation_pair_id": "rot_..._a" | null,
      "trim_reason": "concentration_cap" | "rotation_pair" | "signal_decay" | null,
      "scaling_factor": 1.0,
      "audit_notes": []
    }
  ],
  "rejected_recommendations": [
    {
      "isin": "...",
      "source_recommendation": "...",
      "rejected_because": "string (specific rule)",
      "would_have_been": "string (e.g. 'Buy 20000 kr')"
    }
  ],
  "capital_summary": {
    "portfolio_value_kr": number,
    "cash_available_kr": number,
    "sell_proceeds_kr": number,
    "total_deployable_kr": number,
    "total_buy_amount_kr": number,
    "cash_remaining_kr": number,
    "cash_floor_kr": number,
    "cash_above_floor_kr": number,
    "cash_policy": {
      "floor_pct": 0.05,
      "regime_override_active": false,
      "regime_used": "Stagflation"
    }
  },
  "constraint_violations": [
    {
      "type": "string (enum)",
      "isin": "string | null",
      "value": "any",
      "message": "string (≤2 sentences)"
    }
  ]
}
```

## Configuration consumed
- `config-10-portfolio.json` → entire file

## Vocabulary owned

| `trade` | Meaning |
|---|---|
| `Buy` | Open new position with fresh capital |
| `TopUp` | Add to existing held position (below target weight) |
| `Trim` | Reduce existing held position (concentration cap or signal decay) |
| `Sell` | Close held position fully |
| `PartialSell` | Reduce held position partially (Weakness with Partial thesis) |
| `Hold` | Held position with directional signal but already at target — no action |
| `NoOp` | Not held + Skip recommendation, or rejected by gating — no action |

## Recommendation → Trade conversion

The conversion table expands the six recommendation types into seven trade types based on portfolio state.

| Recommendation | Held? | Below target? | Trade emitted | Trade reason |
|---|---|---|---|---|
| CatalystEntry | no | n/a | **Buy** | catalyst_entry_fresh |
| CatalystEntry | yes | yes | **TopUp** | catalyst_entry_topup |
| CatalystEntry | yes | no (at/above target) | **Hold** | catalyst_entry_at_target |
| MomentumEntry | no | n/a | **Buy** | momentum_entry_fresh |
| MomentumEntry | yes | yes | **TopUp** | momentum_entry_topup |
| MomentumEntry | yes | no | **Hold** | momentum_entry_at_target |
| ThesisExit | yes | n/a | **Sell** | thesis_exit_full |
| ThesisExit | no | n/a | **NoOp** | thesis_exit_not_held |
| MomentumExit | yes | n/a | **PartialSell** or **Sell** (see sizing) | momentum_exit_decay |
| MomentumExit | no | n/a | **NoOp** | momentum_exit_not_held |
| Maintain | yes | n/a | **Hold** | maintain_default |
| Maintain | no | n/a | **NoOp** | maintain_no_position |
| Skip | yes | n/a | **Hold** | skip_held |
| Skip | no | n/a | **NoOp** | skip_default |

Plus one cross-cutting case:

| Trigger | Trade emitted | Trade reason |
|---|---|---|
| Held position exceeds `max_position_pct_of_portfolio` (regardless of recommendation) | **Trim** | concentration_cap |
| Held sector exposure exceeds `max_sector_pct_of_portfolio` | **Trim** on largest fund in that sector | sector_cap |

## Cash policy enforcement

The cash floor is the minimum cash held as a fraction of total portfolio value. Default 5% always; macro override raises it during stressed regimes (default off).

```
cash_floor_kr = portfolio_value_kr × cash_policy.floor_pct
cash_above_floor_kr = cash_available_kr − cash_floor_kr   (clamped at 0)
total_deployable_kr = cash_above_floor_kr + sell_proceeds_kr
```

The floor is enforced strictly. Buys cannot dip into reserved cash, even for a high-conviction CatalystEntry.

### Why cash floor defaults to 5% with macro override OFF

The macro override mechanism (raise floor to 10–15% during Stagflation/Crisis) was tested against a full year of Schroder data. Result: every variant net-lost vs the fixed 5% floor.

| Variant | Avg cash held | Drawdown saved | Opportunity cost | **Net** |
|---|---|---|---|---|
| Fixed 5% | 5.0% | baseline | baseline | baseline |
| Naive override (no confirmation) | 6.8% | +0.73% | −1.99% | **−1.26%** |
| Confirmed 2 windows | 5.2% | +0.32% | −2.01% | **−1.69%** |
| Hysteresis | 8.3% | +0.73% | −2.81% | **−2.08%** |

The pattern: regime tags fire AFTER drawdowns close, not before — you raise cash near the bottom and lower it near the top, which is exactly backwards. The override stays in config (so it can be re-enabled with bear-cycle data) but defaults off.

5% baseline is enough operational dry powder for new BuySignals during normal weeks, with negligible drag (∼0.3% on the test universe).

## Conviction gating

Before sizing, recommendations are filtered through conviction thresholds to prevent low-quality trades.

| Rule | Default threshold | Behavior |
|---|---|---|
| Skip Sell if `conviction < skip_sell_below_conviction` | 0.40 | UNLESS `thesis_validity = Invalid` (those are decisive exits regardless of conviction) |
| Skip Buy if `conviction < skip_buy_below_conviction` | 0.30 | Always — buys are reversible, no exception clause |

Filtered recommendations land in `rejected_recommendations[]` with reason text. They do not appear in `trades[]`.

### Why these defaults

The conviction-gated sell rule is the **defense-in-depth against the Taiwan / China A / Glb Clmt Chg false-positive pattern**. In our earlier chain output, three Sells fired on funds with strong Sharpe (+8.5, +16.9, +17.2) but only 2/3 positive windows — pure noise rotation. Their UniverseEnricher convictions land at ~0.31 / 0.34 / 0.36, all below 0.40 → all filtered. Meanwhile the clean sells (Indian Opports, Emerging Europe, Energy with Invalid thesis) score 0.62+ → all execute.

The asymmetry (sells gated higher than buys) reflects that selling is more costly: triggering a tax event, breaking a position you may want back later. Buys are reversible — you can always exit if the trade doesn't work. The exception clause (`thesis_validity = Invalid` overrides the conviction floor) preserves the rotation case: when the thesis is broken, exit decisively regardless of conviction.

## Sizing logic

The pipeline does NOT receive explicit `amount_kr` from upstream. PortfolioConstructor sizes trades based on:

1. **Default Buy target.** Each Buy gets `default_buy_target_pct × total_deployable_kr` (default 5%).
2. **Conviction-rank scaling.** When `Σ buys > total_deployable_kr`, scale all buys proportionally by `total_deployable_kr / Σ buys`. Highest-conviction buys keep closest to their requested amount.
3. **Min trade dropout.** Any buy that falls below `min_trade_kr` (default 5,000) is dropped; the freed cash redistributes to remaining buys.
4. **Sells.** ThesisExit emits Sell with `amount_kr = current_value_kr` (full exit). MomentumExit emits PartialSell at 50% of position by default; if conviction ≥ 0.7, escalates to full Sell.

### Worked sizing example — chain output bug fixed

The earlier chain output we examined had this issue:

| Field | Value |
|---|---|
| `cash_available_kr` | 50,000 |
| `sell_proceeds_kr` | 0 |
| `total_buy_amount_kr` | 60,000 (3 Buys × 20,000 each) |
| `cash_remaining_kr` | **−10,000** ← BUG |

Under the spec's sizing logic with cash floor 5%:

| Step | Calculation |
|---|---|
| `portfolio_value_kr` | 50,000 (cash only, assume no held positions for simplicity) |
| `cash_floor_kr` | 50,000 × 0.05 = 2,500 |
| `cash_above_floor_kr` | 50,000 − 2,500 = 47,500 |
| `total_deployable_kr` | 47,500 + 0 = 47,500 |
| Default per Buy at 5% × deployable | 2,375 each — but 3 buys = 7,125 total |
| Default per Buy at 33% (since 3 buys exist) | 15,833 each |
| Σ buys at 15,833 each | 47,499 (within deployable) |
| Final | Em Mkts 15,833 / Glb Em Mkt Opps 15,833 / QEP Glbl Qual 15,833 |
| `cash_remaining_kr` | 47,500 − 47,499 = 1 (rounding) |

The constraint violation `cash_remaining_kr_negative` cannot occur because sizing scales buys to fit deployable BEFORE emission, not after.

## Rotation pair handling

When a Sell and a Buy share the same `rotation_pair_id` (assigned by UniverseEnricher), they form a rotation pair. The default policy (`execute_atomically_if_possible = true`):

- Sell proceeds fund the paired Buy first; remaining proceeds go to other Buys or cash above floor.
- If the Buy half cannot execute (sector cap, concentration, conviction floor): Sell still executes, proceeds go to cash, and a `rotation_pair_partial` constraint violation is raised.
- If the Sell half cannot execute (already not held — a NoOp): the Buy still executes from cash above floor, no violation.

## Worked example — full chain run

Continuing the Global Energy → Glbl Alt Engy rotation:

| Fund | Recommendation | Conviction | rotation_pair_id |
|---|---|---|---|
| Global Energy | ThesisExit | 0.91 | rot_2026_W18_a |
| Glbl Alt Engy | CatalystEntry | 0.78 | rot_2026_W18_a |
| Em Mkts | MomentumEntry | 0.71 | null |
| Frontier Mkts | MomentumEntry | 0.41 | null |

State: portfolio_value 850,000 kr; cash_available 50,000; Energy held at 25,000.

| Step | Computation |
|---|---|
| Conviction gating | All four pass (0.91 / 0.78 / 0.71 / 0.41 all > 0.40 sells / 0.30 buys) |
| Sell Energy | `Sell` 25,000 kr, sell_proceeds_kr = 25,000 |
| `cash_floor_kr` | 850,000 × 0.05 = 42,500 |
| `cash_above_floor_kr` | 50,000 − 42,500 = 7,500 |
| `total_deployable_kr` | 7,500 + 25,000 = 32,500 |
| Three Buys at 33% each | 10,833 each → 32,499 total ✓ |
| Rotation pair | Glbl Alt Engy gets first claim on Sell proceeds; full 10,833 funded from Energy sell |
| Other Buys | Em Mkts 10,833 + Frontier 10,833 from remaining proceeds + cash |
| Output | 1 Sell + 3 Buys, no constraint violations |

## Constraint violations

Anything the agent cannot resolve cleanly is surfaced explicitly rather than silently glossed.

| Type | When it fires |
|---|---|
| `cash_remaining_kr_negative` | Should never fire after correct sizing — would indicate a code bug |
| `position_exceeds_concentration_cap` | After execution, a held position is above `max_position_pct_of_portfolio`; agent emits a Trim |
| `sector_exceeds_cap` | Sector concentration above `max_sector_pct_of_portfolio`; agent refuses incremental Buys in that sector |
| `rotation_pair_partial` | One leg of a rotation pair was rejected; the other executed alone |
| `conviction_gate_unusual` | A high-conviction (≥0.8) Sell was filtered (rare; means thesis was Valid despite Weakness) |
| `min_trade_dropout` | A buy was dropped because scaled amount fell below `min_trade_kr` |
| `held_fund_not_in_universe` | positions.csv has a fund with no record in the agent input — log, don't halt |

## Failure modes

| Trigger | Behavior |
|---|---|
| `09-enrichment` is missing or schema-invalid | Halt — `10-error-{iso_week}-{run_id}.json` |
| `config-10-portfolio.json` config_version doesn't match | Halt — config drift |
| Σ conviction weights drift (config-09 issue) | Already caught by step 09; this agent receives a halt notification |
| Cash floor cannot be maintained even after dropping all buys | Emit empty `trades[]`, surface as `cash_floor_unmet` violation |
| Held fund's `current_value_kr` is null or negative | Halt — data integrity error |

## Test fixtures

| Scenario | Inputs | Expected |
|---|---|---|
| Clean chain | 16 funds, 3 Buys + 1 Sell + held positions | 4 trades, no violations |
| Buys exceed deployable | 5 Buys totaling 200% of deployable | Scale all buys; one drops below min_trade and is removed; 4 trades emitted |
| Conviction-gated sell | 1 Sell with conviction 0.31 | Rejected; appears in `rejected_recommendations` |
| Conviction-gated sell with Invalid thesis | 1 Sell with conviction 0.31 BUT thesis_validity = Invalid | Executed despite conviction floor (exception clause) |
| Rotation pair both sides execute | Sell-Buy in same theme, both pass gates | Both trades emitted with same rotation_pair_id |
| Rotation pair, Buy side fails sector cap | Sell + Buy paired, sector at cap | Sell executes alone, `rotation_pair_partial` violation |
| Concentration cap breach | Held position 12% of portfolio | Trim trade emitted with `trim_reason = concentration_cap` |
| Cash bug (chain output regression) | 3 Buys, total > available cash | Buys scaled to fit; no negative cash_remaining |
| Macro override on (testing only) | macro_override_enabled = true, regime = Crisis | Floor raised to 15%, fewer/smaller buys |

## Edge cases

- A fund with `recommendation = Maintain` and `currently_held = false` — should not occur (Recommender prevents it), but defensively emit `NoOp` with `audit_notes` flagging the inconsistency.
- A held fund that doesn't appear in step 09 enrichment (filtered out earlier): emit `Hold` to preserve the position; raise `held_fund_not_in_universe` violation; do not Trim.
- A `Sell` on a fund with `current_value_kr = 0` (held but zero value — rare): emit `NoOp`, log warning.
- Multiple rotation pairs with overlapping themes: each gets its own letter suffix (rot_..._a, _b, _c). PortfolioConstructor handles them independently — the atomicity is per-pair, not across all rotations.
- Empty universe (no recommendations of any type): emit empty `trades[]`, capital_summary still populated, no violations.
- Only Sells, no Buys: cash builds up; floor is automatically respected; no violation.
- Only Buys, no Sells: cash drained from above-floor; if buys exceed cash above floor, sized down by conviction rank.
- A high-conviction (>0.8) Sell on a non-held fund (NoOp anyway): conviction gating doesn't fire; agent emits NoOp with the audit note "would have been Sell but not held".

## What this agent does NOT do

| NOT in scope | Where it lives instead |
|---|---|
| LLM-based reasoning of any kind | step 09 (UniverseEnricher) — alternatives differentiator text |
| Picking which fund to Sell among multiple held in a Weakness theme | step 09 — universe_rank decides |
| Generating trade rationales beyond `trade_reason` tags | step 09 — those should already be in conviction_breakdown / signal narratives |
| Cross-week consistency checks (was Buy executed last week?) | A separate operational layer; PortfolioConstructor is stateless across runs |
| Settlement, broker integration, order routing | Outside this pipeline entirely |
