# Agent 01: DataLoader

> Load, validate, and normalize the four CSV input files into a single per-fund record set keyed by ISIN.

## Execution type
⚙️ Code

## Inputs

| Source | What for |
|---|---|
| `inputs/YieldRaccoon_metadata_{family}_{iso_week}.csv` | Static fund identity (fee, category, risk, owners) |
| `inputs/YieldRaccoon_summary_{family}_{iso_week}.csv` | Per-bucket bi-weekly history (~26 rows per fund) |
| `inputs/YieldRaccoon_snapshot_{family}_{iso_week}.csv` | Rolling 12-week and 1-year aggregates (1 row per fund) |
| `inputs/positions.csv` | Currently held funds + Cash row (may be empty otherwise) |
| `inputs/portfolio_structure.md` | Layer definitions and pinned funds (`core`, `writeoff`) |

### `positions.csv` schema

| Column | Type | Notes |
|---|---|---|
| `isin` | string | Foreign key to metadata. **Required for fund rows.** Omitted on the `Cash` row. |
| `name` | string | Human-readable fund name. Optional but recommended for diffs. The `Cash` row uses literal `Cash` here. |
| `current_value_kr` | number | Current market value in SEK |
| `cost_basis_kr` | number | Original purchase value, for P&L |

The literal row `Cash, , <value>, <value>` (or any row whose `name` equals `Cash` and has no ISIN) is **not** a fund — it's the user's available cash. DataLoader carves this row out and emits its `current_value_kr` as a separate `cash_available_kr` field (see Output schema). PortfolioConstructor reads it directly.

### Input expectations
- All three CSV files share the same `{family}` and `{iso_week}` tags in their filenames.
- The metadata, summary, and snapshot files each contain a consistent set of ISINs (the producer applies the same filter across all three).
- `positions.csv` references ISINs that must exist in metadata; ISINs not in metadata fail validation. The `Cash` row is exempt from this check.
- `portfolio_structure.md` lists pinned funds by name (and optionally ISIN). Names must match `metadata.name` exactly (case-sensitive); ISINs, when present, take precedence.

## Outputs

### Output file
Pattern: `01-dataloader-{iso_week}-{run_id}.json`

### Output schema

```json
{
  "generated_at": "ISO 8601 timestamp",
  "iso_week": "YYYY-Www",
  "family": "string",
  "run_id": "string",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "string",
      "metadata": {
        "name": "string",
        "company_name": "string",
        "currency_code": "string",
        "category": "string",
        "fund_type": "string",
        "is_index_fund": true | false | null,
        "managed_type": "ACTIVE" | "PASSIVE",
        "total_fee": number,
        "management_fee": number,
        "risk": int | null,
        "rating": int | null,
        "sharpe_ratio_static": number | null,
        "standard_deviation_static": number | null,
        "recommended_holding_period": "string (enum)",
        "capital": number,
        "number_of_owners": int
      },
      "nav_buckets": [
        {
          "period_start": "YYYY-MM-DD",
          "period_end": "YYYY-MM-DD",
          "first_nav": number,
          "last_nav": number,
          "nav_high": number,
          "nav_low": number,
          "return_2w_pct": number,
          "ann_volatility_2w_pct": number,
          "max_drawdown_2w_pct": number,
          "current_drawdown_pct": number,
          "sharpe_2w": number | null,
          "best_day_pct": number,
          "worst_day_pct": number,
          "pct_positive_days": number,
          "skewness": number
        }
      ],
      "snapshot": {
        "as_of_date": "YYYY-MM-DD",
        "return_12w_compound_pct": number | null,
        "ann_volatility_12w_pct": number | null,
        "sharpe_12w": number | null,
        "max_drawdown_12w_pct": number | null,
        "return_1y_compound_pct": number | null,
        "ann_volatility_1y_pct": number | null,
        "sharpe_1y": number | null,
        "max_drawdown_1y_pct": number | null
      } | null,
      "currently_held": true | false,
      "current_value_kr": number | null,
      "cost_basis_kr": number | null,
      "layer": "core" | "active"
    }
  ],
  "frozen_positions": [
    {
      "name": "string (matched against metadata.name)",
      "isin": "string | null (null if portfolio_structure.md pinned by name only)",
      "current_value_kr": number,
      "cost_basis_kr": number | null,
      "reason": "string (e.g. 'frozen — cannot trade')"
    }
  ],
  "cash_available_kr": number,
  "data_quality": {
    "metadata_rows": int,
    "summary_rows": int,
    "snapshot_rows": int,
    "positions_rows": int,
    "writeoff_count": int,
    "core_count": int,
    "warnings": []
  }
}
```

## Configuration consumed
None. This agent reads files and emits joined records; downstream agents apply config.

## Vocabulary owned

| Enum | Values | Meaning |
|---|---|---|
| `layer` | `core`, `active` | `core` = pinned long-term holding (e.g. monthly-savings target). `active` = standard tradeable fund. **`writeoff` funds are excluded from `funds[]` entirely** and surfaced via `frozen_positions[]`. |

## What it does

1. Verify the three CSV input filenames carry the same `{iso_week}` tag.
2. Load each CSV with strict schema validation (per `inputs/FUND-STATISTICS-EXPORT-AGENT-GUIDE.md`).
3. Parse `inputs/positions.csv`:
   - Carve out the `Cash` row (literal name `Cash`, no ISIN) → emit its `current_value_kr` as the top-level `cash_available_kr` field.
   - Remaining rows are fund holdings, joined into the universe by ISIN.
4. Parse `inputs/portfolio_structure.md`:
   - Build a name→layer (and optional ISIN→layer) map from the pinned-funds table.
   - Funds tagged `writeoff` are removed from `funds[]` and emitted as entries in `frozen_positions[]` instead — their value still counts toward portfolio totals downstream, but they never enter scoring or trade construction.
   - Funds tagged `core` are kept in `funds[]` with `layer: "core"`; PortfolioConstructor will refuse to emit Sell/Trim for them.
   - All other funds default to `layer: "active"`.
5. Group summary buckets by ISIN, sort each group by `period_end` ascending.
6. Rename the static metadata Sharpe columns to `sharpe_ratio_static` and `standard_deviation_static` to avoid collision with computed Sharpe values downstream.
7. Join metadata, summary buckets, snapshot row, and (non-Cash, non-writeoff) positions on `isin`.
8. Emit one fund record per ISIN with all source data inlined, plus `frozen_positions[]`, `cash_available_kr`, and `data_quality` totals.

For 16 Schroder funds, output is 16 fund records (minus any writeoff matches), each containing ~26 NAV buckets and one snapshot row. Total around 25K tokens.

## Failure modes

| Trigger | Behavior |
|---|---|
| Filename `{iso_week}` mismatch across the three CSVs | Halt — emit `01-error-{iso_week}-{run_id}.json` with diagnosis |
| Schema drift in any CSV (missing column, wrong type) | Halt — schema integrity error |
| ISIN appears in `summary.csv` but not in `metadata.csv` | Warn, drop orphan summary rows, continue |
| ISIN appears in `positions.csv` but not in `metadata.csv` (and is not the `Cash` row) | Halt — held position must be a known fund |
| ISIN appears in `metadata.csv` but missing from `snapshot.csv` | Warn, set `snapshot: null` on that fund record; downstream conviction penalizes |
| Empty `positions.csv` (header only) | Valid; all funds get `currently_held: false`, `cash_available_kr: 0` |
| `Cash` row missing | Valid; `cash_available_kr: 0` and warning logged |
| Multiple `Cash` rows | Halt — ambiguous |
| `portfolio_structure.md` references a name that doesn't match any `metadata.name` | Warn, skip that pinning; do not halt |
| `portfolio_structure.md` missing entirely | Valid; all funds default to `layer: "active"`, `frozen_positions: []` |
| Numeric field contains `NaN` | Preserve as JSON `null`; do not coerce to 0 |

## Test fixtures

| Scenario | Inputs | Expected output |
|---|---|---|
| Happy path | All inputs present, 16 funds, 8 held + Cash row, 1 writeoff pinning matches a held fund | 15 fund records in `funds[]` (writeoff excluded), 7 with `currently_held: true`, 1 entry in `frozen_positions[]`, `cash_available_kr` set from Cash row, no warnings |
| Cash only | `positions.csv` has only the Cash row | All funds `currently_held: false`, `cash_available_kr` = Cash row value, `frozen_positions: []` |
| Missing snapshot row | snapshot.csv missing 1 ISIN that's in metadata | Record present with `snapshot: null` and a warning logged |
| Filename mismatch | summary file is `2026-W17`, others are `2026-W18` | Halt with explicit error file |
| Empty positions | `positions.csv` only has header | All funds `currently_held: false`, `cash_available_kr: 0`, no error |
| Core pinning | `portfolio_structure.md` pins 2 funds to `core` | Those funds emit with `layer: "core"`; rest are `active` |
| Writeoff pinning + held | A held fund is pinned to `writeoff` | Removed from `funds[]`; entry in `frozen_positions[]` carries its value |
| Writeoff pinning + not held | Pinned `writeoff` fund is not in positions.csv | Filtered from `funds[]` silently; no `frozen_positions` entry |
| Pinning name typo | `portfolio_structure.md` has a name with no match in metadata | Skip with warning, do not halt |
| NaN in summary Sharpe | One bucket has `sharpe_2w = NaN` | Preserved as `null` in JSON |

## Edge cases
- `family = "all"` exports may have ~1,400 funds; output structure is identical, just larger.
- `recommended_holding_period` enum value not in known set: pass through unmodified, downstream agents handle.
- Funds with `nav_buckets` shorter than 3 entries: emit normally; SignalScorer treats `windows_total < 3` as a separate case.
- `metadata.csv` may have empty `rating` or `sharpe_ratio_static`; map to JSON `null`, never zero.
- ISIN is the primary join key for fund rows; case-sensitive match. The `Cash` row is the only exception — matched by `name == "Cash"` and absence of ISIN.
- A `core`-pinned fund that is not currently held still emits with `layer: "core"` — downstream agents see it but PortfolioConstructor will only ever Buy/TopUp, never Sell.
- A `writeoff`-pinned fund that is not currently held is filtered out and produces no `frozen_positions[]` entry (nothing to track).
- `portfolio_structure.md` is human-edited markdown — parse defensively (skip blank rows, trim whitespace, accept either name-only or name+ISIN columns).
