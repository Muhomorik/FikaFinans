# Fund Signals — Interactive Chart Specification

This document is the build contract for the interactive fund-signals chart. It captures every layout, color, encoding, popup-structure, and inference-rule decision so the chart can be produced end-to-end from the inputs in this folder, without re-eliciting any visual choices.

---

## Purpose

A single self-contained HTML page that visualises one ISO-week's fund universe as time-series lines, color-coded by step-4 signal label, with each line styled and marked to encode which agent(s) drove the recommendation. Clicking any line opens a modal with one section per pipeline step.

---

## Inputs

The build reads four data files plus two configs from this folder.

Data:
- `inputs/YieldRaccoon_summary_<family>_<isoweek>.csv` — 2-week NAV time series per ISIN. Used for the chart's y-values.
- `inputs/YieldRaccoon_snapshot_<family>_<isoweek>.csv` — point-in-time 12-week metrics per ISIN. Used by the inline step-9 conviction calculation (sharpe_12w, max_drawdown_12w_pct, ann_volatility_12w_pct).
- `examples/chain-step5-fund-signals-*.json` — per-fund step 4–7 fields. Treat **shape** as authoritative; treat **label values** as a stand-in until the real SignalScorer runs (the example file predates the post-mortem rule fix in `config-04-signals.json` and contains known false-positive Sells on Taiwan, China A, and Glb Clmt Chg).
- `examples/chain-step6-actions-*.json` — per-fund step-8 action records plus the portfolio-level `capital_summary`.

Configs:
- `config-09-conviction.json` — weights and metrics-quality thresholds for step-9 conviction.
- `config-10-portfolio.json` — buy/sell conviction floors, min trade, scaling rules.

> **Not directly consumed but shape the upstream pipeline output the chart will eventually receive:** `inputs/positions.csv` (now `isin, name, cost_basis_kr, current_value_kr` plus a `Cash` row carved into `cash_available_kr`) and `inputs/portfolio_structure.md` (assigns `layer ∈ {core, writeoff, active}` to pinned funds). Writeoff funds never appear in `funds[]` — they live in a separate `frozen_positions[]` array and only surface in the portfolio-level capital summary. Core funds appear normally with `layer: "core"` and are blocked from Sell/Trim by step 10.

---

## Output

Produce a single HTML file (any filename) and place it in this folder. Hard requirements:

- Standalone: the page renders correctly when opened in any modern browser, with no companion files referenced from disk.
- Single file deliverable. CSS lives in a `<style>` block; JS lives in `<script>` blocks; no separate `.css`/`.js` files.
- Use Chart.js v4. Prefer inlining the UMD bundle — some viewers (e.g. Cowork's preview iframe) block external CDN loads, so inlining is the portable default. CDN loading is fine when you know the runtime environment allows it.

To obtain the bundle without network access, fetch the npm tarball for `chart.js@4.4.1` and use `dist/chart.umd.js` from inside it.

---

## Page layout

White background, no gradients, no shadows. Vertical stack:

1. Header (top, bordered bottom).
2. Chart area (fills remaining viewport, `min-height: 420px`, `position: relative`).
3. Modal overlay (initially hidden; dimmed full-page background, centered card up to 680px wide, max 90vh, scrollable inside).

### Header rows — three rows, stacked

1. Page title: `Fund signals - interactive`.
2. **Window / Macro row** — muted text. Format:
   - `Window <YYYY-MM-DD -> YYYY-MM-DD>` then `|` then `Macro: <macro_regime>`
3. **Line legend row** — prefix `line:` muted, then in order, `|`-separated into two groupings:
   - Label → line color (5 entries, each a 28px colored stroke + label): `Pre-analysis` (gray), `BuySignal` (green), `SellSignal` (red), `Watch` (orange), `Pass` (gray).
   - WHY → stroke pattern (4 entries, each a short stroke in the pattern + name): `trigger` (solid), `macro` (dashed), `catalyst` (dotted), `both` (dash-dot).
4. **Dot legend row** — prefix `dot:` muted, then four entries — 14px endpoint marker shape + name:
   - filled circle: `trigger`
   - 5-point star: `macro (= macro-aligned)`
   - rotated square (diamond): `catalyst (= catalyst-driven)`
   - filled triangle: `both`

The Pre-analysis swatch in row 3 represents the gray historical portion of every line; it is not a label classification.

### Color palette (single source of truth)

| Element | Hex |
|---|---|
| BuySignal | `#1F8A3D` |
| SellSignal | `#C2362A` |
| Watch | `#E08E1A` |
| Pass | `#7A7A7A` |
| Pre-analysis (gray) | `#C9CCD0` |
| Border / divider | `#e3e5e9` |
| Muted text | `#5b6068` |
| Body text | `#111418` |

Endpoint markers in the chart use the same color as the line.

---

## Chart

Chart.js v4 line chart.

- **X axis**: linear scale of epoch-millisecond timestamps. Tick callback formats as `YYYY-MM-DD`. About 8 ticks max. No date adapter is required.
- **Y axis**: linear. Title: `NAV (rebased to 100)`. Subtle grid at ~5% opacity.
- **Window**: last 6 months relative to the latest `period_end` across all funds. Each fund's NAV is rebased to 100 at its first observation inside the window.
- **Datasets**: each fund contributes two datasets sharing the analysis_start point so segments connect:
  - Pre-analysis: gray (`#C9CCD0`), 1.4px stroke, no point markers, points only where `period_end <= analysis_start`.
  - Analysis: signal-label color, 2px stroke, stroke pattern from WHY taxonomy, endpoint marker shape from WHY taxonomy. Points only where `period_end >= analysis_start`.
- **Analysis window**: the 3 most-recent 2-week periods per fund.
- **Endpoint emphasis**: only the last point of the analysis dataset uses the special marker; in-between points are small circles. Endpoint radius ~5.5px, in-between radius ~2.2px.
- **Tooltip**: date (`YYYY-MM-DD`), fund name, signal label in brackets, NAV value. Filtered to skip the gray pre-analysis dataset.
- **Click handler**: clicking any line segment opens the modal for that fund.
- **No native Chart.js legend** — the HTML rows above handle this.
- **No animation** is required, but Chart.js defaults are fine.

### WHY classification (per fund)

A single derived value: `trigger`, `macro`, `catalyst`, `combined`, or `none`.

Rules, evaluated in this order:
1. If label is `Pass` → `none`.
2. If `catalyst` is non-null AND macro is contributing (rotation_target_alignment ∈ {Strong, Partial} OR label is Watch) → `combined`.
3. Else if `catalyst` is non-null → `catalyst`.
4. Else if rotation_target_alignment ∈ {Strong, Partial} OR label is Watch → `macro`.
5. Else → `trigger`.

| WHY | Stroke pattern (Chart.js borderDash) | Endpoint marker (Chart.js pointStyle) |
|---|---|---|
| trigger | solid (`[]`) | `circle` |
| macro | dashed (`[6,4]`) | `star` |
| catalyst | dotted (`[2,3]`) | `rectRot` (diamond) |
| combined | dash-dot (`[6,3,2,3]`) | `triangle` |
| none | solid | `circle` |

The WHY taxonomy compresses the (macro × catalyst) cross-product into 4 visible buckets and is intentionally lossy on Strong-vs-Partial macro and Direct-vs-Indirect catalyst exposure. Conviction score (step 9) is the place where those finer distinctions are preserved numerically.

---

## Popup (modal) structure

Modal opens centered, dimmed page behind. Closes on: X button, outside-card click, or Escape key.

### Modal header

- `<h2>` fund name + colored signal-label pill + muted `why: <why-label>` text. Why-labels: `trigger`, `macro-aligned`, `catalyst-driven`, `macro + catalyst`, `no signal`.
- Subtitle line, muted: ISIN · category.
- Close button (×) top-right.

### Modal body — ten sections in pipeline order

Each section has a left vertical 3px gray border with 10px padding-left, and an uppercase muted h3 heading `Step N · <AgentName>`. Steps 9 and 10 carry a small gray "inferred inline" badge in the heading until the real agents are implemented.

1. **DataLoader** — table: isin, name, category, currently_held, current_value_kr, layer, below_threshold. Render `layer` as a small uppercase pill: `CORE` (muted blue), `ACTIVE` (no styling, plain text). If the field is absent (legacy example data), render the row as italic muted "—".
2. **MetricsCalculator** — table: windows_positive, current_drawdown_pct, latest_sharpe, ann_volatility_pct, net_return_after_fee_pct, total_fee_pct.
3. **MacroAnalyst** — table: macro_regime, net_mood_input, data_period_end (portfolio-level; identical across funds). `net_mood_input` is the upstream `analytics-weekly-summary.netMood` value preserved by step 03 — useful when it differs from the derived `macro_regime`. If absent (legacy example data), omit the row.
4. **SignalScorer** — table: label (rendered as colored pill), missing_for_upgrade, rule_rationale.
5. **MacroAligner** — table: rotation_target_alignment.
6. **CatalystTagger** — table: exposure_type, catalyst. If catalyst is null show italic muted "No catalyst tagged in this run."
7. **ThesisValidator** — table: thesis_validity.
8. **Recommender** — green action card with: bold action verb, validity, amount in kr (Swedish thousand-separator), rationale paragraph, optional `alt:` line in muted color. If no action: italic muted "No action emitted (Recommender returned NoOp / Skip)."
9. **UniverseEnricher** (inferred inline) — table: conviction_score, universe_rank, data_quality_flag.
10. **PortfolioConstructor** (inferred inline) — table: recommendation_type, final_allocation_kr (Swedish kr formatting), optional gate_reason. Followed by a small muted heading "portfolio-level capital summary:" and a sub-table: active_positions_value_kr, frozen_positions_value_kr, cash_available_kr, sell_proceeds_kr, total_deployable_kr, total_buy_amount_kr, cash_remaining_kr. Omit `active_positions_value_kr` and `frozen_positions_value_kr` rows if absent in the source (legacy example data).

All numeric kr amounts must be rendered with `Math.round(n).toLocaleString('sv-SE')` and " kr" suffix. Numeric metrics (drawdown_pct, sharpe, etc.) render to 4 decimal places. `null` values render as italic muted "null".

All user-controlled strings (rationales, names, categories) must be HTML-escaped.

---

## Step 9 inference rules (UniverseEnricher)

Computed for every fund and written into the fund's signal record before the JSON is embedded in the page.

**Conviction score** is the dot product of weights from `config-09-conviction.json` against five components, then clamped to [0, 1] and rounded to 3 decimals.

| Component | Default weight | Computation |
|---|---:|---|
| signal_strength | 0.25 | BuySignal/SellSignal → 1.0; Watch → 0.5; Pass → 0.3. |
| metrics_quality | 0.25 | Start with `sharpe_12w / sharpe_12w_normalization_max`, clamped to [0, 1]. Multiply by 0.7 if `max_drawdown_12w_pct < drawdown_penalty_threshold_pct`. Multiply by 0.85 if `ann_volatility_12w_pct > vol_penalty_min_pct`. |
| macro_alignment | 0.15 | Strong → 1.0; Partial → 0.5; None → 0.0. |
| thesis_validity | 0.20 | Valid → 1.0; Partial → 0.66; NotApplicable → 0.5; Invalid → 0.0. |
| universe_context | 0.15 | Fixed at 0.5 in this preview. Real step 9 should peer-rank within metadata.category against the snapshot's percentile distribution. |

**Universe rank** is computed within label group, sorted descending by conviction; rank 1 = highest conviction.

**Data quality flag**: `missing_snapshot` if no snapshot row exists for the ISIN; `partial` if sharpe_12w or ann_volatility_12w_pct is null in the snapshot; otherwise `ok`.

---

## Step 10 inference rules (PortfolioConstructor)

**Layer gate (applied first).** Read the fund's `layer` field. If absent (legacy example data), default to `active`. If `layer == "core"` and the classification below would yield `ThesisExit` or `MomentumExit`, downgrade to `Maintain` and set `gate_reason = "core_locked"` — core funds are protected from Sell/Trim. `TopUp`, `CatalystEntry`, `MomentumEntry` on a core fund are permitted (Buy is allowed). Funds with `layer == "writeoff"` never reach this stage (they are filtered into `frozen_positions[]` upstream and don't appear in `funds[]`).

**Recommendation type** classification — exactly one of:

| Condition | Type |
|---|---|
| BuySignal + held | `TopUp` |
| BuySignal + not held + has catalyst | `CatalystEntry` |
| BuySignal + not held + no catalyst | `MomentumEntry` |
| SellSignal + not held | `Skip` |
| SellSignal + held + thesis Invalid + layer ≠ core | `ThesisExit` |
| SellSignal + held + thesis Invalid + layer = core | `Maintain` (gate_reason: `core_locked`) |
| SellSignal + held + thesis ≠ Invalid + layer ≠ core | `MomentumExit` |
| SellSignal + held + thesis ≠ Invalid + layer = core | `Maintain` (gate_reason: `core_locked`) |
| Watch + held | `Maintain` |
| Watch + not held | `Skip` |
| Pass | `NoOp` |

(Real step 10 emits `NoOp` for not-held weakness signals, not `Skip` — this is a small fidelity gap to fix when replacing the inline preview.)

**Final allocation in kr**:

For Buy types (`MomentumEntry`, `CatalystEntry`, `TopUp`):
- If `conviction_score < skip_buy_below_conviction`, allocation is 0 and `gate_reason` records the conviction value vs the floor.
- Otherwise the fund's target is its step-8 `amount_kr`.
- If sum of targets across all qualifying buys ≤ `total_deployable_kr`, each gets its full target.
- Otherwise, conviction-weighted proportional scaling: each fund's share = `(target × conviction) / sum(target × conviction)`; allocation = share × deployable. Any allocation below `min_trade_kr` becomes 0 with a `gate_reason`.

For Sell types (`MomentumExit`, `ThesisExit`):
- Skip if `conviction_score < skip_sell_below_conviction`, UNLESS `thesis_validity == Invalid` AND `skip_sell_below_conviction_unless_thesis_invalid` is true.
- Otherwise allocation = step-8 `amount_kr`.

For all other types (`NoOp`, `Skip`, `Maintain`, `Hold`, etc.): allocation = 0.

---

## Implementation notes

- The HTML must surface JS errors visibly. Wrap chart construction in a try/catch and pipe any thrown error into a red error banner above the canvas. Also attach a global `window.error` handler. Include a "Loading chart…" boot indicator that disappears once the chart renders, so a stuck or never-running script is obvious.
- User-controlled strings (rationales, names, categories) in the popup must be HTML-escaped.
- Numeric values that reach the screen are rounded: kr amounts to whole numbers (sv-SE locale), fractional metrics to 4 decimals.
- The data payload (the fund records the chart consumes) lives in a single `<script id="payload" type="application/json">` block. This avoids escaping issues that arise when interpolating JSON inline into JS.
- The macro_regime string in step 3 of the popup is portfolio-level — it is identical across all funds and read from the step-5 example JSON's top-level field, not from each fund record.

---

## Known issue — verify on every rebuild

In some environments (notably Cowork's Write/Edit file tooling) writes silently truncate above certain sizes. We've observed JS source files cut at ~4–7 KB and Python files cut at ~14 KB and padded with NUL bytes after the last good byte. The build script then runs to apparent success and produces a multi-hundred-KB HTML file, but the inline `<script>` block ends mid-token and the page renders the header/legend rows with no chart — and the JS error never surfaces because the truncation cuts off the script that wires up the error banner.

Required checks before declaring the chart done:
1. After writing any source file, confirm it ends with valid syntax (closing braces/brackets balanced, no trailing NUL bytes, byte count consistent with the intended content).
2. Independently parse-check the inline JS embedded in the produced HTML (e.g. extract each `<script>` body and run a syntax checker over it).
3. If any written file appears truncated, rewrite it through a shell heredoc or another path that bypasses the file-tool cap. Do not rely on a "successfully wrote" status alone.
