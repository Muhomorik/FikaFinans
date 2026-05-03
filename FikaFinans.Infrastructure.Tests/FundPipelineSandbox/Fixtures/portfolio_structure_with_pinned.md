# Portfolio Structure (Step 6 fixture)

Layer definitions and permanent fund pinnings used for the action-consolidation
sandbox tests. Pairs with `sample_fund_signals.json`.

## Layer definitions

- **Core** — default destination for monthly savings. Global index funds with low fees only; 1-2 funds total. Rebalanced at most once per year.
- **Writeoff** — cannot trade for technical reasons (frozen, sanctioned). Ignore in all calculations.

## Pinned funds

| Fund                       | Layer | Note                                  |
| -------------------------- | ----- | ------------------------------------- |
| Pinned Core Global Index   | core  | Global index anchor — monthly savings |
