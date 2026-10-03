# NUMBERS.md

Every non-trivial constant used by the implementation.

| Constant | Value | Why this value |
|---|---:|---|
| Overdraft fee | AED 25.00 | Directly specified by the requirement; using half (AED 12.50) would violate the supplied rule. |
| Daily interest rate | 0.04% / 0.0004 | Directly specified by the requirement; half would be 0.02%, changing every accrual. |
| AED precision | 2 dp | Directly specified; one decimal or three decimals would violate currency precision. |
| BHD precision | 3 dp | Directly specified; two decimals would lose valid BHD precision. |
| Window | 6 days | Directly specified as Day 1 through Day 6. |
| Opening ACC-001 | AED 0.00 | Directly specified. |
| Opening ACC-002 | BHD 0.000 | Directly specified. |
| E10 installments | 3 | Directly specified as three equal instalments; exact-equality wording conflicts with BHD precision and is documented in REJECTED.md. |

## Rounding

`decimal.Round(..., MidpointRounding.AwayFromZero)` is used for currency amounts.

This is explicit rather than relying on the runtime default midpoint behavior.

For E10, the exact BHD 10.000 total cannot be represented by three identical 3-decimal amounts. The implementation therefore uses the deterministic largest-remainder split:

- 3.334
- 3.333
- 3.333

This preserves the booked total exactly.
