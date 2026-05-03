# Agent 01: DataLoader

> Load, validate, and normalize the four CSV input files into a single per-fund record set keyed by ISIN.

## Execution type
⚙️ Code

## Inputs

| Source | What for |
|---|---|
| `YieldRaccoon_metadata_{family}_{iso_week}.csv` | Static fund identity (fee, category, risk, owners) |
| `YieldRaccoon_summary_{family}_{iso_week}.csv` | Per-bucket bi-weekly history (~26 rows per fund) |
| `YieldRaccoon_snapshot_{family}_{iso_week}.csv` | Rolling 12-week and 1-year aggregates (1 row per fund) |
| `positions.csv` | Currently held funds (may be empty) |

### Input expectations
- All four input files share the same `{family}` and `{iso_week}` tags in their filenames.
- The metadata, summary, and snapshot files each contain a consistent set of ISINs (the producer applies the same filter across all three).
- `positions.csv` may reference ISINs that exist in metadata; ISINs not in metadata fail validation.

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
      "acquired_at": "YYYY-MM-DD" | null,
      "cost_basis_kr": number | null
    }
  ],
  "data_quality": {
    "metadata_rows": int,
    "summary_rows": int,
    "snapshot_rows": int,
    "positions_rows": int,
    "warnings": []
  }
}
```

## Configuration consumed
None. This agent reads files and emits joined records; downstream agents apply config.

## Vocabulary owned
None.

## What it does

1. Verify all four input filenames carry the same `{iso_week}` tag.
2. Load each CSV with strict schema validation (per `FUND-STATISTICS-EXPORT-AGENT-GUIDE.md`).
3. Group summary buckets by ISIN, sort each group by `period_end` ascending.
4. Rename the static metadata Sharpe columns to `sharpe_ratio_static` and `standard_deviation_static` to avoid collision with computed Sharpe values downstream.
5. Join metadata, summary buckets, snapshot row, and positions on `isin`.
6. Emit one fund record per ISIN with all source data inlined.

For 16 Schroder funds, output is 16 fund records, each containing ~26 NAV buckets and one snapshot row. Total around 25K tokens.

## Failure modes

| Trigger | Behavior |
|---|---|
| Filename `{iso_week}` mismatch across the four files | Halt — emit `01-error-{iso_week}-{run_id}.json` with diagnosis |
| Schema drift in any CSV (missing column, wrong type) | Halt — schema integrity error |
| ISIN appears in `summary.csv` but not in `metadata.csv` | Warn, drop orphan summary rows, continue |
| ISIN appears in `positions.csv` but not in `metadata.csv` | Halt — held position must be a known fund |
| ISIN appears in `metadata.csv` but missing from `snapshot.csv` | Warn, set `snapshot: null` on that fund record; downstream conviction penalizes |
| Empty `positions.csv` | Valid; all funds get `currently_held: false` |
| Numeric field contains `NaN` | Preserve as JSON `null`; do not coerce to 0 |

## Test fixtures

| Scenario | Inputs | Expected output |
|---|---|---|
| Happy path | All four files, 16 funds, 8 held | 16 fund records, 8 with `currently_held: true`, no warnings |
| Missing snapshot row | snapshot.csv missing 1 ISIN that's in metadata | Record present with `snapshot: null` and a warning logged |
| Filename mismatch | summary file is `2026-W17`, others are `2026-W18` | Halt with explicit error file |
| Empty positions | `positions.csv` only has header | All funds `currently_held: false`, no error |
| NaN in summary Sharpe | One bucket has `sharpe_2w = NaN` | Preserved as `null` in JSON |

## Edge cases
- `family = "all"` exports may have ~1,400 funds; output structure is identical, just larger.
- `recommended_holding_period` enum value not in known set: pass through unmodified, downstream agents handle.
- Funds with `nav_buckets` shorter than 3 entries: emit normally; SignalScorer treats `windows_total < 3` as a separate case.
- `metadata.csv` may have empty `rating` or `sharpe_ratio_static`; map to JSON `null`, never zero.
- ISIN is the primary join key everywhere; case-sensitive match.
