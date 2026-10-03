# AMBIGUITIES.md

## 1. When is a back-valued overdraft fee assessed?

E7 is processed on Day 5 but has Value Day 2. The rule says the fee is assessed once per day when that day's closing ledger balance is negative.

**Resolution:** assess the Day-2 fee when E7 is replayed, because E7 changes the Day-2 closing ledger balance. The fee itself is appended with Value Day 2.

This also makes the acceptance criterion's “E7 causes exactly one overdraft fee ... on Day 2” coherent.

## 2. What does “evaluated at end of Day 5 and before any fee is assessed” mean?

The Day-2 balance includes all entries with Value Day <= 2, including a Day-5 event that was back-valued to Day 2. The phrase “before fee” is therefore treated as a checkpoint of the balance before adding the fee entry.

**Resolution:** E7's raw Day-2 balance is -370.00 before the fee; then the AED 25 fee is appended.

## 3. Does a reversal reverse fees?

The reversal points specifically to E7. No event-level fee reversal instruction exists.

**Resolution:** E9 reverses only E7. The previously assessed fee remains an append-only ledger entry.

## 4. What does “settles for” mean for an authorization?

A settlement consumes an authorization's hold and posts the settlement amount as a debit.

**Resolution:** settlement reduces the hold by the settlement amount. Auth-A therefore remains partially settled with AED 15.00 held.

## 5. What happens when settlement references an unknown authorization?

The event stream explicitly contains Auth-Z without a preceding authorization.

**Resolution:** reject the settlement, record an error, and append no debit.

## 6. How should a failed authorization affect the ledger?

A hold is not a ledger entry, so a declined authorization should not alter the ledger balance.

**Resolution:** Auth-B is recorded as DECLINED with zero remaining hold.

## 7. How are three equal BHD instalments represented?

BHD has 3 decimal places. 10.000 / 3 = 3.333333..., so three identical 3-decimal values cannot sum to 10.000.

**Resolution:** use largest-remainder allocation: 3.334, 3.333, 3.333. The deliberately failing test documents the conflicting requirement.

## 8. When is daily interest capitalized?

The requirement says daily interest accrues daily but “capitalizes as a single credit at end of Day 6.”

**Resolution:** calculate each day's rounded accrual without adding it to the ledger, then append one Day-6 interest credit equal to the sum of those rounded accruals.

## 9. What does “remainder” mean if rounded daily accruals differ from an unrounded total?

The requirement says the rounded daily accruals must sum exactly to the capitalized total.

**Resolution:** the capitalized total is defined as the sum of the rounded daily accruals. No remainder is discarded.

## 10. Are authorization holds value-dated?

The requirement says a hold is applied when the authorization is approved and available balance must account for active holds.

**Resolution:** a hold becomes active from its authorization event day. It does not change ledger balance.
