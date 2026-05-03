# Agent 04: SignalScorer

> Map per-fund metrics to a single Signal label (Strength / Weakness / Forming / Neutral) using deterministic rules.

## Execution type

⚙️ Code

## Inputs

| Source | What for |
| --- | --- |
| `02-metrics-{iso_week}-{run_id}.json` | Per-fund metrics from MetricsCalculator |
| `config-04-signals.json` | Buy / sell / watch rule thresholds |

## Outputs

### Output file

Pattern: `04-signal-{iso_week}-{run_id}.json`

### Output schema

Adds `signal`, `rule_fired`, and `criteria_evaluation` to each fund record. All prior fields preserved.

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "...",
      "metadata": { /* from step 01 */ },
      "nav_buckets": [ /* from step 01 */ ],
      "snapshot": { /* from step 01 */ },
      "metrics": { /* from step 02 */ },
      "signal": "Strength" | "Weakness" | "Forming" | "Neutral",
      "rule_fired": "string (see taxonomy below)",
      "criteria_evaluation": {
        "buy_3of3_passed": bool,
        "buy_max_dd_passed": bool,
        "buy_min_sharpe_12w_passed": bool,
        "sell_sharpe_2w_lt_0": bool,
        "sell_dd_lt_threshold": bool,
        "sell_pos_le_1": bool,
        "watch_partial_with_macro_alignment": bool,
        "missing_for_upgrade": "string | null",
        "data_quality_warnings": ["..."]
      }
    }
  ]
}
```

## Configuration consumed

- `config-04-signals.json` → entire file

## Vocabulary owned

| Signal | Meaning |
| --- | --- |
| `Strength` | All buy criteria met cleanly. Candidate for entry |
| `Weakness` | At least one sell trigger fired. Candidate for exit |
| `Forming` | Partial signal, missing one buy criterion AND macro alignment is Strong. Worth watching |
| `Neutral` | No directional signal. Includes insufficient-history and conflicting-signal cases |

## Rule logic

### Buy → Strength

All four conditions must hold:

| Condition | Default | Source |
| --- | --- | --- |
| `windows_positive_count == 3` | 3 of 3 | metrics |
| `windows_total == 3` | 3 | metrics |
| `current_drawdown_pct >= max_drawdown_pct` | ≥ −1.0 | metrics vs config |
| `sharpe_12w >= min_sharpe_12w` | ≥ 0.5 | metrics vs config |

If all pass: `signal = Strength`, `rule_fired = buy_3of3_zero_dd`.

### Sell → Weakness

Any one of three triggers fires (most decisive when multiple fire):

| Trigger | Threshold | Rule fired tag |
| --- | --- | --- |
| `sharpe_2w < 0` | 0 | `sell_sharpe_negative` |
| `current_drawdown_pct < -1.5` | −1.5 | `sell_drawdown_breach` |
| `windows_positive_count <= 1` | 1 | `sell_pos_le_1` |

If two or more fire: `rule_fired = sell_combined`.

### Watch → Forming

Only fires when:

- Exactly one buy criterion fails (so the fund nearly qualifies as Strength)
- AND `macro_alignment >= Strong` (which comes from MacroAligner — but for v1, this rule cannot fire because MacroAligner is downstream of SignalScorer)

> **Sequencing note for v1.** Because MacroAligner runs after SignalScorer, the `Watch / Forming` label cannot be assigned in step 04 — it would require macro alignment which doesn't exist yet. Implementations should either:
>
> 1. **Defer Forming assignment** — emit `signal = Neutral` here and let MacroAligner promote near-buy candidates to Forming downstream.
> 2. **Reorder** — move MacroAligner before SignalScorer in v2.
>
> v1 takes option 1: `Forming` is assigned by MacroAligner, not SignalScorer. The taxonomy below reflects this.

### Default → Neutral

If neither buy nor any sell trigger fires: `signal = Neutral`, `rule_fired = neutral_default`.

Special Neutral cases:

- `windows_total < 3` → `rule_fired = neutral_insufficient_history`
- Buy criteria met AND sell trigger fired → `rule_fired = neutral_conflicting`

## Why these defaults

Background on each threshold so future tuning has context:

| Setting | Default | Rationale |
| --- | --- | --- |
| `min_sharpe_12w = 0.5` | 0.5 | Filters out marginal momentum. Most live Buys observed had Sharpe 1.5+. Tighten to 1.0 if too many low-quality Buys leak through |
| `max_drawdown_pct = -1.0` | −1.0 | Allows a small dip without disqualifying a Strength signal. The fund may be off its peak by ≤1% and still qualify |
| `sell sharpe_2w < 0` | 0 | Fast change-detector. The 2-week Sharpe is noisy by itself, but a clean sign-flip is meaningful |
| `sell current_drawdown_pct < -1.5` | −1.5 | Tighter than 0; not so tight that a single bad bucket triggers an exit. The Indian Opports clean-sell case at −1.97% would fire under this |
| `sell windows_positive_count <= 1` | 1 | One or zero positive buckets in last three is a clear momentum failure |

The any-of structure of the sell rule (versus all-of) is the post-mortem fix for the **Taiwan / China A / Glb Clmt Chg false-positive pattern**. Earlier versions fired Weakness when `thesis = Partial AND windows < 3/3` — that was wrong because Taiwan had Sharpe +8.5, vol healthy, drawdown 0, but only 2/3 windows positive due to one negative bucket. Tightened any-of rule means a fund needs *one* of three concrete failures, not just an incomplete window count.

## What it does — worked examples

### Global Energy (LU0256331488) — Weakness

| Metric | Value | Result |
| --- | --- | --- |
| `windows_positive_count` | 2 of 3 | buy_3of3 ✗ |
| `current_drawdown_pct` | −5.48 | sell_dd_lt_threshold ✓ |
| `sharpe_2w` | −3.59 | sell_sharpe_2w_lt_0 ✓ |
| `windows_positive_count <= 1` | (2 > 1) | sell_pos_le_1 ✗ |

Two sell triggers fire → `signal = Weakness`, `rule_fired = sell_combined`.

### Em Mkts (LU0106252389) — Strength

| Metric | Value | Result |
| --- | --- | --- |
| `windows_positive_count` | 3 of 3 | buy_3of3 ✓ |
| `current_drawdown_pct` | 0.00 | buy_max_dd ✓ |
| `sharpe_12w` | +1.82 | buy_min_sharpe_12w ✓ |

All buy criteria pass → `signal = Strength`, `rule_fired = buy_3of3_zero_dd`.

### Taiwanese Eq (LU0270814014) — Neutral (the false-positive guard)

| Metric | Value | Result |
| --- | --- | --- |
| `windows_positive_count` | 2 of 3 | buy_3of3 ✗ |
| `sharpe_2w` | +8.53 | sell_sharpe_2w_lt_0 ✗ |
| `current_drawdown_pct` | 0.00 | sell_dd_lt_threshold ✗ |
| `windows_positive_count <= 1` | (2 > 1) | sell_pos_le_1 ✗ |

No sell trigger fires; one buy criterion missing → `signal = Neutral`, `rule_fired = neutral_default`. (Promoted to `Forming` by MacroAligner if macro alignment is Strong.)

This is the case the post-mortem was about — under the old rule, Taiwan would have fired Weakness purely due to `pos < 3 + thesis_partial`. The any-of rule keeps it Neutral.

## Failure modes

| Trigger | Behavior |
| --- | --- |
| Fund record has no `metrics` (DataLoader / MetricsCalculator skipped it) | Set `signal = Neutral`, `rule_fired = neutral_no_data`, log warning |
| `metrics.windows_total < 3` | `rule_fired = neutral_insufficient_history` |
| `metrics.sharpe_2w` is `null` (NaN) AND config `treat_nan_sharpe_as_zero_for_rules = true` | Treat as 0 for rule evaluation; record warning in `criteria_evaluation.data_quality_warnings` |
| Buy criteria all met AND sell trigger also fires | `rule_fired = neutral_conflicting`, signal = `Neutral` |
| Config sum / threshold drift (e.g. config_version mismatch) | Halt — `04-error-{iso_week}-{run_id}.json` |

## Test fixtures

| Scenario | Input metrics | Expected |
| --- | --- | --- |
| Clean Strength | pos 3/3, dd 0, sharpe_12w 1.5 | `Strength`, `buy_3of3_zero_dd` |
| Clean Weakness (deep dd) | pos 2/3, dd −5.5, sharpe_2w −3.5 | `Weakness`, `sell_combined` |
| Mild momentum decay | pos 1/3, dd −2.0, sharpe_2w −0.5 | `Weakness`, `sell_combined` |
| False-positive guard | pos 2/3, dd 0, sharpe_2w +8 | `Neutral` |
| Insufficient history | pos 0/2, windows_total 2 | `Neutral`, `neutral_insufficient_history` |
| Conflict (buy + sell) | pos 3/3, dd 0, sharpe_12w 1.5, sharpe_2w −0.5 | `Neutral`, `neutral_conflicting` |
| NaN sharpe_2w (vol guard fired upstream) | pos 3/3, sharpe_2w null, dd 0 | `Strength` (NaN treated as 0, so sell_sharpe_2w_lt_0 doesn't fire) |

## Edge cases

- A fund with `windows_total == 0` (no NAV history at all): `signal = Neutral`, `rule_fired = neutral_no_data`. PortfolioConstructor will refuse trades in this fund regardless.
- A fund with all NaN sharpe values across all horizons: emit Neutral; UniverseEnricher's conviction scoring will assign a low score via `data_quality.sharpe_*_is_nan` flags.
- The `criteria_evaluation.missing_for_upgrade` field is filled when `signal = Neutral` and the fund is "close" to Strength — e.g. "needs 3rd positive window" or "needs sharpe_12w to reach 0.5". MacroAligner uses this field to decide Forming promotion.
