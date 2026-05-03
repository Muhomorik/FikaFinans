# snapshot.csv — Per-Week Schema Plan

A specification for the weekly fund snapshot CSV. Each file captures the rolling-horizon state of every fund in the universe at a single evaluation date. One file per week, written once and never modified.

---

## Design constraints

Two constraints govern every choice in this spec:

| Constraint | Implication |
|---|---|
| The CSV must fit in an LLM's context window | One row per fund. No bucket history. No daily NAV. Compact column set |
| Cloud agents have no access to the WPF producer | Whatever the agent needs at evaluation time must be present in the file. No on-demand recomputation |
| One file per week, write-once | Each weekly file is immutable after emission. Backtesting and audit replay both rely on this property |

The result is a small, point-in-time digest: trailing-window aggregates anchored at a single date, computed once by the producer and shipped to the cloud.

---

## Table of contents

1. [Purpose](#1-purpose)
2. [File lifecycle](#2-file-lifecycle)
3. [Naming convention](#3-naming-convention)
4. [Schema](#4-schema)
5. [Field semantics](#5-field-semantics)
6. [Edge case handling](#6-edge-case-handling)
7. [Validation expectations](#7-validation-expectations)
8. [Producer responsibilities](#8-producer-responsibilities)
9. [Consumer access pattern](#9-consumer-access-pattern)

---

## 1. Purpose

The fund pipeline needs rolling-horizon metrics (12-week and 1-year Sharpe, volatility, return, drawdown) at evaluation time. These cannot be computed reliably from `summary.csv`'s per-bucket aggregates and cannot be computed at all from inside the cloud agent (no daily NAV access). The snapshot file is where those values live.

`summary.csv` answers "how has this fund moved over time?" — many rows per fund, full bi-weekly history. `snapshot.csv` answers "what is this fund's state right now?" — one row per fund, latest aggregates only. Together they cover history and current-state without overlap.

A new snapshot file is generated once per week. Old files are retained locally on the WPF workbench (for backtesting) but only the current week's file is needed for live cloud operation.

---

## 2. File lifecycle

### Production

The WPF Statistics Export tool generates one snapshot file per ISO week. The intended cadence is once per week, on a fixed weekday (e.g. Monday morning), evaluating against the most recent daily NAV available.

### Immutability

Once a snapshot file is written, **it is not modified.** Re-running the producer for the same ISO week should produce a byte-identical file (subject to producer determinism). If the producer is corrected or improved, prior snapshot files remain as-is — the correction applies only to going-forward output.

This immutability is what makes backtesting trustworthy: the snapshot file for week N reflects what the producer believed at week N, not what it would say today about week N.

### Retention

| Location | Retention policy |
|---|---|
| WPF workbench (local) | Indefinite — used for backtesting and audit |
| Cloud (where agents read) | Latest week only is required; prior weeks may be uploaded for backtest runs |

The WPF user decides which historical files to upload to the cloud and when. Cloud agents only require the current week's file for live operation.

---

## 3. Naming convention

### Format

`YieldRaccoon_snapshot_{family}_{iso_week}.csv`

Where:
- `{family}` is the fund family (e.g. `Schroder`, `Storebrand`), or `all` if the export was unfiltered
- `{iso_week}` is the ISO 8601 week designation (`YYYY-Www`)

### Examples

- `YieldRaccoon_snapshot_Schroder_2026-W18.csv`
- `YieldRaccoon_snapshot_Storebrand_2026-W18.csv`
- `YieldRaccoon_snapshot_all_2026-W14.csv`

### Why ISO week

All weekly artifacts in the system share the `{iso_week}` tag — `metadata-{iso_week}.csv`, `summary-{iso_week}.csv`, `snapshot-{iso_week}.csv`, and the three macro markdown reports (which carry `iso_week` in their YAML metadata). A "week bundle" — the complete set of files for one ISO week — is identifiable by simple filename match. Cross-file joining by week needs no other key.

ISO weeks are also unambiguous across years: Week 1 of 2027 cannot be confused with Week 53 of 2026.

The `as_of_date` column inside the file specifies the exact day within the week the snapshot was taken — typically the last trading day of the prior week, but the file content is authoritative.

---

## 4. Schema

Ten columns total. Identity, evaluation date, and eight rolling-horizon metrics across two horizons.

| Column | Type | Unit | Notes |
|---|---|---|---|
| `isin` | string | — | Join key to `metadata.csv` and `summary.csv` |
| `as_of_date` | date | YYYY-MM-DD | Evaluation date — the exact day this snapshot reflects |
| `return_12w_compound_pct` | number | percent | Compound return over trailing 12 weeks (84 days) ending at `as_of_date` |
| `ann_volatility_12w` | number | percent (annualized) | Annualized volatility of daily returns over trailing 12 weeks |
| `sharpe_12w` | number | annualized ratio | Risk-adjusted return at 12-week horizon |
| `max_drawdown_12w_pct` | number | percent (non-positive) | Worst peak-to-trough drawdown within trailing 12 weeks |
| `return_1y_compound_pct` | number | percent | Compound return over trailing 52 weeks ending at `as_of_date` |
| `ann_volatility_1y` | number | percent (annualized) | Annualized volatility over trailing 52 weeks |
| `sharpe_1y` | number | annualized ratio | Risk-adjusted return at 1-year horizon |
| `max_drawdown_1y_pct` | number | percent (non-positive) | Worst peak-to-trough drawdown within trailing 52 weeks |

One row per fund. All eight metric columns may be `NaN` when fund history is insufficient for the horizon.

---

## 5. Field semantics

This section defines what each numeric field *means* — i.e. what value a consumer should expect when reading it. The producer is free to compute these values however it likes, but the resulting numbers must match these definitions.

All horizon-based fields are anchored at `as_of_date` looking backward. The window for "12 weeks" is exactly 84 calendar days; for "1 year" it is 365 calendar days.

### 5.1 Compound return at horizon H

`return_H_compound_pct` is the total percentage change in NAV from `as_of_date − H days` to `as_of_date`, expressed as a percent. Compounded, not summed.

If the fund's history begins later than `as_of_date − H days`, the value is `NaN`.

### 5.2 Annualized volatility at horizon H

`ann_volatility_H` is the standard deviation of daily log-returns over the H-day window, scaled to an annual figure (multiplied by √252) and expressed as a percent.

If the window contains insufficient samples, the value is `NaN`.

### 5.3 Sharpe at horizon H

`sharpe_H` is the annualized excess return divided by annualized volatility:

> sharpe_H = (annualized_return_H − risk_free_rate) / ann_volatility_H

For the 12-week horizon, `annualized_return_12w` is the geometric annualization of `return_12w_compound_pct`. For the 1-year horizon, `annualized_return_1y` ≈ `return_1y_compound_pct` (the window is already roughly one year).

The producer chooses which `risk_free_rate` to use (annualized short rate appropriate to the fund's currency, or constant zero for v1). The choice must be consistent across all funds in the file and documented in the producer's own documentation. A constant zero is acceptable provided it is applied uniformly.

### 5.4 Max drawdown at horizon H

`max_drawdown_H_pct` is the worst peak-to-trough decline observed within the H-day window, expressed as a non-positive percent. A fund that only rose has `max_drawdown = 0`.

### 5.5 The `as_of_date` field

`as_of_date` is the actual evaluation date the rolling values were computed against — typically the most recent trading day with a daily NAV available at the time of export. It need not be the first or last day of the ISO week; the filename's ISO week is the bucket label, while `as_of_date` is the precise anchor.

All rows in a single snapshot file share the same `as_of_date`.

---

## 6. Edge case handling

### 6.1 History shorter than horizon

When a fund's first observed NAV is more recent than the lookback window:

| Fund age at `as_of_date` | Behavior |
|---|---|
| < 84 days | All `_12w_*` columns = `NaN` |
| < 365 days | All `_1y_*` columns = `NaN` |
| < 14 days | The fund may still appear in the file if it appears in `metadata.csv`, but all eight metric columns = `NaN` |

### 6.2 NaN propagation

Rolling fields must propagate `NaN` upward when the underlying data is incomplete. Never silently zero-fill or substitute. Specifically:

| Field state | Consumer interpretation |
|---|---|
| Numeric value | Trust the value; producer has confirmed the underlying sample was sufficient |
| `NaN` | Insufficient history or invalid input at the producer; treat as missing |

The producer's policy on the upstream daily NAV (gap-filling, outlier handling) is internal. From the consumer's perspective only the resulting digest values matter.

### 6.3 Sharpe with near-zero volatility

When `ann_volatility_H` falls below 0.01 (essentially constant NAV — common for short-duration money-market funds), the Sharpe denominator approaches zero and the ratio explodes.

> If `ann_volatility_H` < 0.01, set `sharpe_H = NaN` rather than emitting a value > 100.

This avoids the chain output behavior we observed previously, where bond funds emitted `sharpe = +41.14` purely on a near-zero denominator.

### 6.4 Funds dropped between weeks

A fund may appear in week N's snapshot but be absent from week N+1's (delisted, fee threshold change, ownership minimum, etc.). This is allowed. Consumers comparing across weeks should handle missing-fund cases — there is no obligation for snapshot files to maintain a stable fund list across weeks.

### 6.5 Funds added between weeks

Symmetrically, a new fund may appear in week N+1 that wasn't in week N. The fund's `_1y_*` columns will likely be `NaN` (insufficient history) on first appearance. Consumers should treat this as new-fund-onboarding rather than a data error.

---

## 7. Validation expectations

### 7.1 Producer-side checks

| Check | Action on failure |
|---|---|
| All 10 column headers present in expected order | Halt — schema drift |
| `as_of_date` is the same value on every row | Halt — point-in-time invariant violated |
| `as_of_date` falls within the ISO week named in the filename | Halt — naming mismatch |
| `isin` is unique within the file | Halt — duplicate fund |
| Numeric fields are either valid numbers or `NaN`, never empty | Halt — type error |
| Rolling fields are `NaN` exactly when history is insufficient | Warn |

### 7.2 Consumer-side checks

| Check | Action on failure |
|---|---|
| File for the requested ISO week exists | Halt — missing input |
| Headers match expected schema | Halt with explicit error |
| `as_of_date` is the same on every row | Halt — file integrity error |
| Every fund in `metadata.csv` (post-filter) appears | Warn — possibly intentional drop, log |
| No fund in snapshot is absent from `metadata.csv` | Halt — orphan fund |

---

## 8. Producer responsibilities

The WPF producer is responsible for:

| Responsibility | Notes |
|---|---|
| Generating one snapshot file per ISO week | Cadence: once per week, fixed weekday |
| Reading daily NAV from its private database | Daily values never leave the producer |
| Computing all rolling aggregates correctly | Per the formulas in [Section 5](#5-field-semantics) |
| Honoring the same fund-eligibility filters as `metadata.csv` and `summary.csv` | `Buyable = 1`, optional company filter, minimum owners — applied identically across all three files for the same export |
| Naming the file with the correct ISO week | Per [Section 3](#3-naming-convention) |
| Writing the file once and not modifying it | Immutability invariant |
| Retaining historical files locally | For backtest replay |

The producer is **not** responsible for uploading files to the cloud, scheduling consumer runs, or deciding which historical files cloud agents need. Those are operational concerns of the WPF user.

---

## 9. Consumer access pattern

Cloud agents (the fund pipeline) access snapshot files via three patterns.

### 9.1 Live weekly run

Read the snapshot file matching the current ISO week. Every row in the file is consulted. Latest `as_of_date` available is used.

| File needed | Why |
|---|---|
| `YieldRaccoon_snapshot_{family}_{current_week}.csv` | Source of all rolling-horizon metrics for SignalScorer, ThesisValidator, conviction scoring |

### 9.2 Backtest run

Read the snapshot file matching the historical week being replayed. Same access pattern, just a different filename.

| File needed | Why |
|---|---|
| `YieldRaccoon_snapshot_{family}_{historical_week}.csv` | Replays the rolling metrics as the producer saw them at that point in time |

The `summary.csv` and `metadata.csv` files used in backtest must be consistent with the snapshot's date — either truncated to that point or, if metadata is treated as static, the current version is acceptable.

### 9.3 Trend analysis (optional, future)

If an agent wants to detect "Sharpe accelerating" or "rolling drawdown deepening over recent weeks," it can load multiple consecutive snapshot files and compare values across `as_of_date` boundaries.

| Files needed | Why |
|---|---|
| `snapshot-{week_N}` ... `snapshot-{week_N+k}` | Time series of point-in-time snapshots |

This is a future use case. The pipeline as currently specified does not require it.

---

*End of plan.*
