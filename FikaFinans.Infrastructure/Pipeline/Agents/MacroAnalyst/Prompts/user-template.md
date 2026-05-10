ISO week (periodIsoWeek): {iso_week}
Upstream run IDs:
- weekly summary runId: {weekly_summary_run_id}
- substitution chain runId: {substitution_chain_run_id}
- rotation targets runId: {rotation_targets_run_id}

Fund categories in universe (use ONLY these for matching):
{category_list}

=== analytics-weekly-summary.json ===
{weekly_summary_json}

=== analytics-substitution-chain.json ===
{substitution_chain_json}

=== analytics-rotation-targets.json ===
{rotation_targets_json}

Return JSON exactly conforming to this schema:
{
  "macro_regime": "RiskOn | RiskOff | Mixed | Stagflation | Crisis",
  "macro_regime_secondary": "string or null",
  "regime_confidence": 0.0,
  "catalysts": [
    {
      "name": "string",
      "intensity": "low | medium | high",
      "weeks_active": 1,
      "affected_categories": ["..."],
      "rationale": "string (≤2 sentences)"
    }
  ],
  "rotation_themes": [
    {
      "label": "string",
      "signal_strength": "Strong | Moderate | Weak",
      "affected_categories": ["..."],
      "rationale": "string (≤2 sentences)",
      "source_chain": {
        "capital_fleeing": "string verbatim from input",
        "flows_toward": "string verbatim from input"
      }
    }
  ]
}
