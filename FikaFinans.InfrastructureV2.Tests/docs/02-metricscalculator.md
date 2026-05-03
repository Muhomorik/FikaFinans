# Agent 02: MetricsCalculator

> Assemble per-fund metrics by combining bucket history, rolling snapshot values, and metadata-derived fee data.

## Execution type

⚙️ Code

## Inputs

| Source | What for |
| --- | --- |
| `01-dataloader-{iso_week}-{run_id}.json` | Joined fund records from DataLoader |
| `config-02-metrics.json` | Primary Sharpe horizon, partial-bucket policy |

## Outputs

### Output file

Pattern: `02-metrics-{iso_week}-{run_id}.json`

### Output schema

Adds a `metrics` object to each fund record. All fields from the prior step are preserved.

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "...",
      "metadata": { /* preserved from step 01 */ },
      "nav_buckets": [ /* preserved */ ],
      "snapshot": { /* preserved */ },
      "currently_held": "...",
      "metrics": {
        "windows_positive_count": int,
        "windows_total": int,
        "current_drawdown_pct": number,
        "ann_volatility_2w_pct": number,
        "sharpe_2w": number | null,
        "sharpe_12w": number | null,
        "sharpe_1y": number | null,
        "ann_volatility_12w_pct": number | null,
        "ann_volatility_1y_pct": number | null,
        "return_12w_compound_pct": number | null,
        "return_1y_compound_pct": number | null,
        "max_drawdown_12w_pct": number | null,
        "max_drawdown_1y_pct": number | null,
        "total_fee_pct": number,
        "net_return_after_fee_12w_pct": number | null,
        "as_of_date": "YYYY-MM-DD" | null,
        "data_quality": {
          "buckets_used": int,
          "snapshot_missing": bool,
          "snapshot_stale_vs_summary": bool,
          "sharpe_2w_is_nan": bool,
          "sharpe_12w_is_nan": bool,
          "sharpe_1y_is_nan": bool
        }
      }
    }
  ]
}
```

## Configuration consumed

- `config-02-metrics.json` → `primary_sharpe_horizon_weeks`, `min_bucket_days`, `drop_partial_buckets`

## Vocabulary owned

None.

## Where each field comes from

| Field | Source |
| --- | --- |
| `windows_positive_count`, `windows_total` | Counted from the last 3 entries of `nav_buckets`. A bucket is "positive" if `return_2w_pct > 0` |
| `current_drawdown_pct`, `ann_volatility_2w_pct`, `sharpe_2w` | Selected from the last entry of `nav_buckets` |
| `sharpe_12w`, `sharpe_1y`, `ann_volatility_12w_pct`, `ann_volatility_1y_pct` | Selected from `snapshot` |
| `return_12w_compound_pct`, `return_1y_compound_pct` | Selected from `snapshot` |
| `max_drawdown_12w_pct`, `max_drawdown_1y_pct` | Selected from `snapshot` |
| `total_fee_pct` | Selected from `metadata.total_fee` |
| `net_return_after_fee_12w_pct` | Computed: `return_12w_compound_pct − total_fee_pct × (12 / 52)` |
| `as_of_date` | Selected from `snapshot.as_of_date` |
| `data_quality` flags | Computed from observed null values and date comparisons |

## What it does — worked example

Schroder ISF Global Energy (LU0256331488):

| Field | Value | Source |
| --- | --- | --- |
| Last 3 buckets returns | +9.78%, −5.48%, +1.50% | `nav_buckets[-3:]` |
| `windows_positive_count` | 2 | Two of three are > 0 |
| `windows_total` | 3 | |
| `current_drawdown_pct` | −5.48 | Latest bucket |
| `sharpe_2w` | −3.59 | Latest bucket |
| `sharpe_12w` | +3.57 | Snapshot row |
| `sharpe_1y` | +1.69 | Snapshot row |
| `return_12w_compound_pct` | +26.26 | Snapshot row |
| `total_fee_pct` | 2.38 | Metadata |
| `net_return_after_fee_12w_pct` | 26.26 − 2.38 × 12/52 ≈ 25.71 | Computed |
| `as_of_date` | 2026-04-30 | Snapshot |

## Failure modes

| Trigger | Behavior |
| --- | --- |
| Snapshot row missing for an ISIN | Set rolling fields to `null`; `data_quality.snapshot_missing = true`; do not halt |
| Snapshot `as_of_date` is more than 14 days behind summary's latest `period_end` | Warn; set `snapshot_stale_vs_summary = true`; do not halt |
| Fewer than 3 buckets in `nav_buckets` | Set `windows_total = bucket_count` (may be 0, 1, 2); downstream handles |
| Latest bucket has `sharpe_2w = null` (NaN) | Preserve `null`; set `sharpe_2w_is_nan = true` |
| Snapshot has any rolling field as `null` | Preserve `null`; set the corresponding `*_is_nan` flag |
| Fund has zero `nav_buckets` and no `snapshot` | All metrics fields `null` except `total_fee_pct`; `windows_total = 0`; downstream agents handle as "no data" |

## Test fixtures

| Scenario | Input | Expected output |
| --- | --- | --- |
| Standard fund | 26 buckets, snapshot present | All metrics populated, no flags |
| New fund | 6 buckets only, snapshot has `sharpe_1y = null` | `sharpe_1y_is_nan = true`, partial output |
| Missing snapshot | Fund in summary but not snapshot | All rolling fields null, `snapshot_missing = true` |
| Bond fund near-zero vol | Producer set `sharpe_2w = NaN` | Preserved as null, `sharpe_2w_is_nan = true` |
| Stale snapshot | snapshot `as_of_date` 20 days behind latest bucket | `snapshot_stale_vs_summary = true`, warn |

## Edge cases

- A fund whose first bucket starts after `as_of_date − 365 days`: `sharpe_1y` and `return_1y_compound_pct` will be `null` in the snapshot. Carry through.
- Net-of-fee return computation uses the simple deduction `total_fee × horizon_weeks / 52`. This is an approximation — consumers comparing fee-adjusted Sharpe should re-derive.
- A fund whose latest non-null `sharpe_2w` is in an older bucket because the latest bucket has NaN: take the latest bucket regardless. Downstream is responsible for understanding the data quality flag.
