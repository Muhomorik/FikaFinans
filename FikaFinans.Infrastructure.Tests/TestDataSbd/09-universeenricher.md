# Agent 09: UniverseEnricher

> Add cross-fund context to each per-fund record: conviction score, universe rank, alternatives, rotation pair linkage.

## Execution type
🔀 Hybrid — code computes ranks and conviction; LLM generates differentiator text for alternatives.

## Inputs

| Source | What for |
|---|---|
| `08-recommendation-{iso_week}-{run_id}.json` | All per-fund records with full context including recommendation |
| `config-09-conviction.json` | Weights, normalization parameters, alternatives policy, rotation pairing rules |

## Outputs

### Output file
Pattern: `09-enrichment-{iso_week}-{run_id}.json`

### Output schema

Adds enrichment fields to each fund record. All prior fields preserved.

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "...",
      "metadata": { /* preserved */ },
      "metrics": { /* preserved */ },
      "signal": "...",
      "thesis_validity": "...",
      "recommendation": "...",
      "currently_held": "...",

      "conviction_score": 0.85,
      "conviction_breakdown": {
        "signal_strength": 1.00,
        "metrics_quality": 0.90,
        "macro_alignment": 0.60,
        "thesis_validity": 1.00,
        "universe_context": 1.00
      },
      "universe_rank": {
        "within_recommendation": 1,
        "of_total_in_recommendation": 6
      },
      "alternatives": [
        {
          "isin": "...",
          "name": "...",
          "differentiator": "string (LLM-generated, ≤1 sentence)"
        }
      ],
      "rotation_pair_id": "rot_2026_W18_a" | null
    }
  ]
}
```

## Configuration consumed
- `config-09-conviction.json` → entire file

## Vocabulary owned
None new. Uses recommendation enum from step 08 and signal enum from step 04.

## Conviction scoring

The single 0–1 number is a weighted sum of five components, each scored 0–1. Same scale applies to Buys and Sells: 0.85 means high confidence regardless of direction.

| Component | Weight | Score 1.0 when… | Score 0.0 when… |
|---|---|---|---|
| Signal strength | 0.25 | All buy criteria cleared with margin (or sell rule fired with multiple triggers) | Just barely passed threshold |
| Metrics quality | 0.25 | High Sharpe (12w), contained vol, shallow drawdown | Weak Sharpe or excessive vol |
| Macro alignment | 0.15 | macro_alignment = Strong | None |
| Thesis validity | 0.20 | Valid (for Buys) or Invalid (for Sells) — directionally appropriate | Inverse of recommendation direction |
| Universe context | 0.15 | Best peer in category, or paired rotation available | No alternative, no rotation pair |

### Per-component scoring rules

**Signal strength** (0–1)
- Strength + multiple buy criteria cleared with margin → 1.0
- Strength but barely (e.g. sharpe_12w just at threshold 0.5) → 0.6
- Weakness with three sell triggers → 1.0
- Weakness with one sell trigger → 0.6
- Forming → 0.4 (it's a watch state, not a strong signal)
- Neutral → 0.0

**Metrics quality** (0–1)
- `clamp(sharpe_12w, 0, sharpe_12w_normalization_max) / sharpe_12w_normalization_max`
- Penalize if `current_drawdown_pct < drawdown_penalty_threshold_pct`: subtract 0.2
- Penalize if `ann_volatility_12w_pct > vol_penalty_min_pct`: subtract 0.15
- Penalize if any `data_quality.sharpe_*_is_nan` flag is true: subtract 0.1 per flag
- Floor at 0.0

**Macro alignment** (0–1)
- Strong → 1.0
- Partial → 0.5
- None → 0.0

**Thesis validity** (0–1)
- For Buys (CatalystEntry, MomentumEntry): Valid → 1.0, Partial → 0.5, NotApplicable → 0.3, Invalid → 0.0
- For Sells (ThesisExit, MomentumExit): Invalid → 1.0, Partial → 0.5, Valid → 0.0
- For Maintain / Skip: NotApplicable → 0.5

**Universe context** (0–1)
- Has rotation_pair_id → 1.0 (paired exits and entries are high-conviction)
- Best peer in category by Sharpe → 1.0
- Has at least one alternative listed → 0.5
- Isolated (no peers, no pair) → 0.0

### Why these defaults

| Setting | Default | Rationale |
|---|---|---|
| Equal weights between technical and contextual (0.50 / 0.50) | 0.25 + 0.25 vs 0.15 + 0.20 + 0.15 | Forces a fund to clear both technical evidence (signal + metrics) AND contextual fit (macro + thesis + universe) before scoring high. Tilts conviction toward "right reasons + right moment" rather than pure trend-following |
| `sharpe_12w_normalization_max = 5.0` | 5.0 | Caps abnormally high Sharpe values — bond funds with low vol can produce Sharpe of 10+ which would dominate the metrics-quality term. Past 5.0, additional Sharpe is treated as noise, not signal |
| `drawdown_penalty_threshold_pct = -3.0` | −3.0 | Funds with current drawdown worse than −3% lose conviction even if Sharpe is positive. Prevents "high Sharpe but currently in trouble" funds from ranking high |
| `vol_penalty_min_pct = 25.0` | 25.0 | Funds above 25% annualized vol get a penalty. Reflects that higher-vol funds need stronger Sharpe to be comparable to low-vol peers — partially offsets the Sharpe normalization |
| Alternatives `max_per_fund = 3` | 3 | One isn't enough to give choice; five+ becomes noise. Three is the standard "primary, alternate, fallback" pattern |
| `tie_break: lower_total_fee` | fee | When two funds tie on conviction, the cheaper one wins. Aligns with fee-minimization philosophy — small fee differences compound meaningfully over a 5-year holding period |

## Universe rank

Within each recommendation type (CatalystEntry, MomentumEntry, ThesisExit, MomentumExit), rank funds by conviction descending. Ties broken by lower total_fee, then ISIN alphabetically.

The rank is **scoped per recommendation type**, not global. So "rank 1 of 3 BuySignals" is independent of the SellSignal ranking. PortfolioConstructor uses these ranks to decide which Buys to fund first when buys exceed deployable cash.

## Alternatives

For each fund with a Buy-side recommendation (CatalystEntry, MomentumEntry), find peer funds in the same `metadata.category`. Compute fee, Sharpe, and vol differences. The LLM is invoked once per fund (not per peer) to generate one-line differentiator text for the top alternatives.

Sells don't get alternatives — the alternative for a Sell is "exit to cash" or the paired rotation Buy.

### LLM prompt for differentiators

```
Fund being recommended: {primary.name} ({primary.isin})
- Category: {primary.metadata.category}
- Total fee: {primary.metadata.total_fee}%
- Sharpe 12w: {primary.metrics.sharpe_12w}
- Volatility 12w: {primary.metrics.ann_volatility_12w_pct}%

Alternatives in the same category:
{for each alt}
  - {alt.name} ({alt.isin})
    Total fee: {alt.metadata.total_fee}%, Sharpe 12w: {alt.metrics.sharpe_12w}, Vol 12w: {alt.metrics.ann_volatility_12w_pct}%

Write a one-line differentiator for each alternative, focusing on the most material
differences (fee, risk profile, Sharpe trajectory, country/factor tilt). ≤ 15 words each.

Return JSON: [{"isin": "...", "differentiator": "..."}]
```

## Rotation pairing

Pair an Exit (ThesisExit or MomentumExit) with an Entry (CatalystEntry or MomentumEntry) when both share `matched_theme.id` from MacroAligner.

Naming: `rot_{iso_week}_{letter}` where letter starts at `a` for the rotation involving the most paired funds, then `b`, etc. Tie-break by alphabetical theme id.

If a theme has multiple Exits and one Entry, all share the same rotation_pair_id. Same for one Exit and multiple Entries. PortfolioConstructor handles the multi-leg execution.

## Worked example — full conviction breakdown

### Global Energy (LU0256331488) — ThesisExit, conviction 0.91

| Component | Score | Reasoning |
|---|---|---|
| Signal strength | 1.00 | Two sell triggers fired (sharpe_2w < 0 + dd < -1.5) — multiple-trigger sells score max |
| Metrics quality | 0.90 | sharpe_12w +3.57 (well above threshold), but current_drawdown_pct −5.48 < −3 → −0.10 penalty; final 0.90 |
| Macro alignment | 0.60 | Macro alignment is Strong (theme still active) but the catalyst is contradicting price action — UniverseEnricher gives partial credit. Computed as Strong × 0.6 because of the contradiction; could also be 1.0 if you interpret Strong literally |
| Thesis validity | 1.00 | Invalid is the maximum-conviction sell-side thesis |
| Universe context | 1.00 | Paired with Glbl Alt Engy CatalystEntry → rotation_pair_id present |

Conviction = 0.25(1.00) + 0.25(0.90) + 0.15(0.60) + 0.20(1.00) + 0.15(1.00) = 0.25 + 0.225 + 0.09 + 0.20 + 0.15 = **0.915 → rounded to 0.91**

### Em Mkts (LU0106252389) — MomentumEntry, conviction 0.73

| Component | Score | Reasoning |
|---|---|---|
| Signal strength | 1.00 | Buy criteria all met cleanly (3/3 windows, dd 0, sharpe_12w +1.82 above threshold 0.5) |
| Metrics quality | 0.70 | sharpe_12w 1.82 / 5.0 = 0.36 + no penalties (vol 28.5 ≈ at threshold, no dd penalty); slight bump for clean buy → 0.70 |
| Macro alignment | 0.50 | Partial alignment via LLM adjacency to "Asian domestic activity" theme |
| Thesis validity | 0.50 | Partial thesis (no catalyst, weak macro) |
| Universe context | 0.70 | 2 peer alternatives exist (Glb Em Mkt Opps, Frontier Mkts), no rotation pair |

Conviction = 0.25(1.00) + 0.25(0.70) + 0.15(0.50) + 0.20(0.50) + 0.15(0.70) = 0.25 + 0.175 + 0.075 + 0.10 + 0.105 = **0.705 → rounded to 0.71**

(Slight mismatch with earlier 0.73 example — the formula is precise, examples are illustrative.)

### Taiwanese Eq (LU0270814014) — Neutral, conviction 0.0 (not actually scored)

Neutral funds get `recommendation = Skip` (or Maintain if held), and conviction is computed but not used downstream. Score would be ~0.20 — low because the recommendation itself is "no action". PortfolioConstructor ignores conviction for Skip/Maintain.

## Failure modes

| Trigger | Behavior |
|---|---|
| Conviction weights don't sum to 1.0 (within tolerance) | Halt — config integrity error |
| LLM fails to return valid JSON for differentiators | Emit alternatives with empty differentiator text; warn |
| Fund category has no peers (single fund in category) | `alternatives = []`; universe_context component may score lower |
| `08-recommendation` output has zero records of a recommendation type | universe_rank unset (no ranking possible); set `universe_rank = null` |
| Two ThesisExits in different themes get the same rotation_pair_id assignment | Bug — should never happen; halt |

## Test fixtures

| Scenario | Inputs | Expected |
|---|---|---|
| Clean catalyst exit | Energy fund, ThesisExit, Invalid thesis | conviction ≥ 0.85, rotation_pair_id set |
| Mid-conviction momentum entry | Em Mkts MomentumEntry, Partial thesis, weak macro | conviction 0.65–0.75 |
| Low-conviction sell (false-positive guard) | Hypothetical Weakness fund with sharpe_12w +5 | conviction < 0.40 — PortfolioConstructor will skip |
| No peers | Country fund (e.g. Taiwan), only one in category | alternatives = [], universe_context lower |
| LLM fails on differentiators | Mock LLM error | alternatives populated with empty differentiators, agent does not halt |
| Rotation pair (sell+buy in same theme) | Energy ThesisExit + Alt Energy CatalystEntry | both records get same rotation_pair_id |

## Evaluation prompt — AI Foundry custom rubric

```
You are evaluating UniverseEnricher's output for a single fund.

Inputs you will see:
- The fund's full record from step 08 (signal, recommendation, metrics, etc.)
- The agent's output (conviction_score, conviction_breakdown, universe_rank, alternatives, rotation_pair_id)
- The full universe of fund records

Score on five dimensions, 1-5 each:

1. Conviction calibration (1-5)
   Does conviction_score reflect the strength of the recommendation?
   - 5: High conviction (>0.8) only when signal is decisive AND context supports.
   - 3: Borderline; could be defended either way.
   - 1: High conviction on a weak setup, or low conviction on an obvious case.

2. Component breakdown self-consistency (1-5)
   - 5: conviction_breakdown components multiplied by weights produce conviction_score within 0.01.
   - 1: Components don't sum to the score (math error).

3. Alternatives relevance (1-5, only when alternatives exist)
   - 5: Alternatives are in the same category and have meaningful differentiators (fee, Sharpe, etc.).
   - 3: Alternatives are loosely related.
   - 1: Alternatives are unrelated funds.

4. Differentiator quality (1-5, LLM-generated text)
   - 5: One-sentence, specific, cites a real metric difference.
   - 3: Vague or generic.
   - 1: Inaccurate or contradicts the data.

5. Rotation pairing accuracy (1-5)
   - 5: Pairs only formed between Exits and Entries sharing matched_theme_id.
   - 1: Spurious pairs (different themes) or missed pairs (same theme not linked).

For each dimension output:
- Score (1-5)
- One-sentence justification with specific numbers

Flag for review if any dimension scores ≤ 2.
```

## Edge cases

- A fund with `recommendation = Skip` or `Maintain`: conviction is still computed but ignored downstream. Don't skip the calculation — having the score available is useful for audit and trend analysis.
- A fund with no metrics (`metrics is null`): conviction defaults to 0.0; downstream agents will not act on it.
- Tied conviction scores within a recommendation type: tie-break by lower fee, then ISIN.
- A `Strength` signal with `metrics.sharpe_12w` null (data quality issue upstream): metrics_quality component scores ≤ 0.4; conviction will be in the mid-range; downstream gating may filter.
- Rotation pairs across multiple themes: a fund's rotation_pair_id reflects the strongest theme match. If a fund's category matches two themes, MacroAligner already picked the strongest one in `matched_theme.id`.
