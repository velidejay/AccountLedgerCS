# In-Memory Account Ledger — C#/.NET

A deliberately small, deterministic account-ledger core implemented in C#.

## Requirements

- .NET 8 SDK
- No database
- No persistence
- No web layer
- No UI

## Run the replay

```bash
dotnet run --project src/AccountLedger/AccountLedger.csproj
```

The console prints, for each Day 1–6:

- closing ledger balance
- available balance
- accumulated fee assessments
- authorization states
- daily interest totals
- errors

It then prints the append-only ledger entries.

## Run the design tests

```bash
dotnet test tests/AccountLedger.Tests/AccountLedger.Tests.csproj --filter "FullyQualifiedName!~Intentionally_Failing"
```

## Run the deliberately failing test

```bash
dotnet test tests/AccountLedger.Tests/AccountLedger.Tests.csproj --filter "FullyQualifiedName~Intentionally_Failing"
```

That test is expected to fail. It documents the impossible acceptance criterion requiring three BHD 3.334 instalments for a BHD 10.000 booking.

## Design

`LedgerCore` stores only append-only `LedgerEntry` records. Reversals append a compensating entry and never mutate or delete the original.

Money uses `decimal`, is rounded to the account currency precision, and uses:

- AED: 2 decimal places
- BHD: 3 decimal places

Authorization holds are separate from ledger entries. Available balance is:

`ledger balance - active holds`

An authorization is approved only when the post-hold available balance is non-negative.

Back-valued events use their `ValueDay` for ledger-balance and fee calculations. The event's processing order remains the supplied stream order.

The Day-2 E7 fee is assessed once when E7 is replayed. E9 later reverses E7 by appending a credit; it does not delete or reverse the already assessed fee.

Daily interest is rounded per account currency for each day. The rounded daily accruals are accumulated and capitalized once on Day 6.
