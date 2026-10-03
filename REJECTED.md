# REJECTED.md

## Acceptance criterion rejected: “After E9, all balances and fees return to their pre-E7 values.”

Rejected because E9 says “reverses E7”, not “reverses E7 and all consequences caused by E7”.

The ledger is explicitly append-only. E7 remains in the ledger, and E9 is a compensating entry. The Day-2 overdraft fee is a separate ledger event that was correctly assessed when E7 made the Day-2 balance negative.

A design that deletes the E7 fee or mutates historical entries would violate the append-only requirement.

## Acceptance criterion rejected: “The three BHD instalments in E10 must each be BHD 3.334.”

Rejected mathematically.

`3.334 + 3.334 + 3.334 = 10.002`

The booked amount is BHD 10.000. Creating BHD 10.002 would invent BHD 0.002.

The implementation uses 3.334 + 3.333 + 3.333 = 10.000. The deliberately failing xUnit test demonstrates the conflict.

## Acceptance criterion rejected: “If the rounded daily interest accruals do not sum to the capitalized total, the remainder is discarded.”

Rejected because it contradicts the explicit requirement that rounded daily accruals must sum exactly to the capitalized total.

The implementation defines the capitalized amount as the sum of the already-rounded daily accruals. No money is silently discarded.

## Approaches abandoned during the build

### Mutating E7 on reversal

Abandoned because the ledger is append-only. The final design leaves E7 intact and appends E9.

### Deleting the Auth-A hold on partial settlement

Abandoned because settlement for AED 185 against an AED 200 hold leaves AED 15 active. Auth-A is therefore PARTIALLY_SETTLED.

### Posting three equal BHD 3.333 amounts

Abandoned because they total BHD 9.999, losing BHD 0.001.

### Posting three BHD 3.334 amounts

Abandoned because they total BHD 10.002, exceeding the booking.

### Computing interest only once from the final Day-6 balance

Abandoned because the requirement specifies daily accruals. Each day's closing balance must be considered separately.

### Using binary floating point

Abandoned because currency arithmetic requires deterministic decimal rounding. C# `decimal` is used throughout.
