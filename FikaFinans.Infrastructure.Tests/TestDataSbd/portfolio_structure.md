# Portfolio Structure

Layer definitions and permanent fund pinnings. Does **not** classify current holdings — those live in `positions.csv` and are treated as **active positions** unless pinned below.

## Layer definitions

- **Core** — default destination for monthly savings. Global index funds with low fees only; 1-2 funds total. Rebalanced at most once per year.
- **Writeoff** — cannot trade for technical reasons (frozen, sanctioned). Ignore in all calculations.

All other holdings are active positions, governed by the buy/sell rules in the analytics prompt.

## Pinned funds

| Fund                                  | Layer    | Note                                  |
| ------------------------------------- | -------- | ------------------------------------- |
| Storebrand Global All Countries A SEK | core     | Global index anchor — monthly savings |
| Storebrand Global Solutions A SEK     | core     | Global index anchor — monthly savings |
| Swedbank Robur Rysslandsfond A        | writeoff | Frozen — cannot trade                 |
