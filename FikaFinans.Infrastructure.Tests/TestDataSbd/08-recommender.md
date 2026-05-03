# Agent 08: Recommender

> Map (signal, thesis_validity, catalyst, currently_held) into a single recommendation type via a deterministic mapping table.

## Execution type
⚙️ Code

## Inputs

| Source | What for |
|---|---|
| `07-thesis-{iso_week}-{run_id}.json` | Per-fund records with full context (signal, macro, catalyst, thesis) |

## Outputs

### Output file
Pattern: `08-recommendation-{iso_week}-{run_id}.json`

### Output schema

Adds `recommendation` and `recommendation_reason` to each fund record. All prior fields preserved.

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "...",
      "metadata": { /* preserved */ },
      "signal": "...",
      "thesis_validity": "...",
      "catalyst": { /* preserved */ },
      "currently_held": true | false,

      "recommendation": "CatalystEntry" | "MomentumEntry" | "ThesisExit" | "MomentumExit" | "Maintain" | "Skip",
      "recommendation_reason": "string (short, structured)"
    }
  ]
}
```

## Configuration consumed
None. Pure deterministic mapping.

## Vocabulary owned

| `recommendation` | Meaning |
|---|---|
| `CatalystEntry` | Strength signal with active Direct catalyst — fundamental driver entry |
| `MomentumEntry` | Strength signal without (Direct) catalyst — pure technical entry |
| `ThesisExit` | Weakness signal with broken thesis (Invalid) — full exit |
| `MomentumExit` | Weakness signal with intact-but-weak thesis (Partial) — exit, but not as decisively |
| `Maintain` | Held position with no directional signal — keep |
| `Skip` | Not held with no directional signal — no action |

## Mapping table

The complete mapping over the four-tuple (signal, thesis_validity, catalyst.exposure_type, currently_held). All combinations are exhaustive — no fund record can fall through.

| Signal | Thesis | Catalyst exposure | Held? | Recommendation |
|---|---|---|---|---|
| Strength | Valid | Direct | any | **CatalystEntry** |
| Strength | Valid | Indirect or null | any | **MomentumEntry** |
| Strength | Partial | Direct | any | **CatalystEntry** |
| Strength | Partial | Indirect or null | any | **MomentumEntry** |
| Weakness | Invalid | any | any | **ThesisExit** |
| Weakness | Partial | any | any | **MomentumExit** |
| Forming | any | any | held | **Maintain** |
| Forming | any | any | not held | **Skip** |
| Neutral | NotApplicable | any | held | **Maintain** |
| Neutral | NotApplicable | any | not held | **Skip** |
| Neutral | any (defensive) | any | held | **Maintain** |
| Neutral | any (defensive) | any | not held | **Skip** |

### Why these mappings

| Mapping | Rationale |
|---|---|
| Strength + Catalyst → CatalystEntry | Fundamental + technical agreement is the highest-conviction entry pattern |
| Strength + no Catalyst → MomentumEntry | Technicals alone are sufficient for entry but signal weaker conviction (UniverseEnricher will score lower) |
| Weakness + Invalid thesis → ThesisExit | Thesis broken means the original investment reason is gone — full exit, no partial holds |
| Weakness + Partial thesis → MomentumExit | Technicals failing but thesis still has some support — exit, but less decisively (PortfolioConstructor may PartialSell rather than full Sell) |
| Forming → Maintain (held) / Skip (not held) | Watch state — don't enter fresh positions, keep existing ones, see if signal develops |

The distinction between **CatalystEntry** and **MomentumEntry** matters downstream because UniverseEnricher uses it for conviction weighting (catalyst presence boosts the macro_alignment + thesis_validity components) and for rotation pairing (CatalystEntry is more likely to be paired with a ThesisExit in the same theme).

## Worked examples

### Global Energy (LU0256331488) — ThesisExit

| Field | Value |
|---|---|
| `signal` | Weakness |
| `thesis_validity` | Invalid |
| `catalyst.exposure_type` | Direct |
| `currently_held` | true |
| `recommendation` | **ThesisExit** |
| `recommendation_reason` | "Weakness + Invalid thesis (catalyst still active but momentum reversed)" |

### Em Mkts (LU0106252389) — MomentumEntry

| Field | Value |
|---|---|
| `signal` | Strength |
| `thesis_validity` | Partial |
| `catalyst` | null |
| `currently_held` | false |
| `recommendation` | **MomentumEntry** |
| `recommendation_reason` | "Strength + Partial thesis, no catalyst — pure momentum entry" |

### Glbl Alt Engy (LU1983299162) — CatalystEntry (Indirect)

Wait — Indirect catalyst with Strength would be **MomentumEntry** under the rule above. The Indirect exposure means the fund isn't a direct beneficiary; it benefits via correlation. Treating Indirect as if there's no catalyst keeps CatalystEntry reserved for high-conviction fundamental drivers.

| Field | Value |
|---|---|
| `signal` | Strength |
| `thesis_validity` | Partial |
| `catalyst.exposure_type` | Indirect |
| `currently_held` | true |
| `recommendation` | **MomentumEntry** |
| `recommendation_reason` | "Strength signal with Indirect catalyst exposure — counted as momentum, not catalyst" |

### Frontier Mkts (LU0562313402) — Skip

| Field | Value |
|---|---|
| `signal` | Neutral |
| `thesis_validity` | NotApplicable |
| `catalyst` | null |
| `currently_held` | false |
| `recommendation` | **Skip** |
| `recommendation_reason` | "Neutral signal, fund not held — no action" |

### Frntr Mkts but held (hypothetical) — Maintain

Same metrics as above but the fund is currently held: `recommendation = Maintain`, `recommendation_reason = "Neutral signal, held position — no change"`.

## Failure modes

| Trigger | Behavior |
|---|---|
| Fund record missing `signal` | Set `recommendation = Skip`, `recommendation_reason = "no_signal_no_action"`, log warning |
| Fund record missing `thesis_validity` | Treat as `NotApplicable` (matrix default for ambiguous cases) |
| Unknown `signal` value (not in enum) | Halt — schema integrity error (`08-error-...json`) |
| Unknown `recommendation` derived (shouldn't happen with exhaustive matrix) | Halt — code bug |

## Test fixtures

| Scenario | Inputs | Expected |
|---|---|---|
| Catalyst entry | Strength + Valid + Direct + not held | CatalystEntry |
| Momentum entry (no catalyst) | Strength + Partial + null + not held | MomentumEntry |
| Momentum entry (Indirect catalyst) | Strength + Partial + Indirect + not held | MomentumEntry |
| Top-up case (held + Strength) | Strength + Valid + Direct + held | CatalystEntry (PortfolioConstructor decides TopUp vs Hold) |
| Thesis exit | Weakness + Invalid + Direct + held | ThesisExit |
| Momentum exit | Weakness + Partial + null + held | MomentumExit |
| Forming held | Forming + Partial + null + held | Maintain |
| Forming not held | Forming + Partial + null + not held | Skip |
| Neutral held | Neutral + NotApplicable + null + held | Maintain |
| Neutral not held | Neutral + NotApplicable + null + not held | Skip |
| Weakness on not-held fund | Weakness + Invalid + any + not held | ThesisExit (downstream PortfolioConstructor converts to NoOp) |

## Edge cases

- **Weakness on a not-held fund** → emits `ThesisExit` or `MomentumExit` even though there's nothing to exit. PortfolioConstructor handles this gracefully (converts to `NoOp` since `currently_held = false`). Don't suppress here — keep the recommendation honest so audit trails reflect the signal.
- **CatalystEntry on a held fund** → emits CatalystEntry; PortfolioConstructor decides whether it's TopUp (under target weight) or Hold (already at target).
- **Forming with Indirect catalyst** → still emits Maintain (held) / Skip (not held). The Indirect catalyst doesn't change the actionability of a Forming signal.
- **Fund with `signal = Weakness` AND `catalyst.exposure_type = Direct` AND `thesis_validity = Partial`** (LLM declined to mark Invalid): emits MomentumExit, not ThesisExit. The thesis isn't fully broken — just weakening.
- The recommendation is a per-fund classification; nothing about portfolio shape, sizing, or pairing happens here. Those are downstream concerns (UniverseEnricher and PortfolioConstructor).
