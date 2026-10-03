using AccountLedger;

var ledger = new LedgerCore();
ledger.AddAccount("ACC-001", Currency.AED, 0.00m);
ledger.AddAccount("ACC-002", Currency.BHD, 0.000m);

var events = new List<LedgerEvent>
{
    new("E1", 1, "CREDIT", "ACC-001", Currency.AED, 1200.00m, 1),
    new("E2", 1, "DEBIT", "ACC-001", Currency.AED, 950.00m, 1),
    new("E3", 2, "AUTHORIZATION", "ACC-001", Currency.AED, 200.00m, 2, "Auth-A"),
    new("E4", 3, "CREDIT", "ACC-001", Currency.AED, 400.00m, 3),
    new("E5", 4, "SETTLEMENT", "ACC-001", Currency.AED, 185.00m, 4, "Auth-A"),
    new("E6", 4, "SETTLEMENT", "ACC-001", Currency.AED, 180.00m, 4, "Auth-Z"),
    new("E7", 5, "DEBIT", "ACC-001", Currency.AED, 620.00m, 2),
    new("E8", 5, "AUTHORIZATION", "ACC-001", Currency.AED, 90.00m, 5, "Auth-B"),
    new("E9", 6, "REVERSAL", "ACC-001", Currency.AED, 0m, 2, null, "E7"),
    new("E10", 5, "CREDIT", "ACC-002", Currency.BHD, 10.000m, 5, null, null, 3)
};

ledger.Replay(events);

for (var day = 1; day <= 6; day++)
{
    var s = ledger.Snapshot(day);
    Console.WriteLine($"DAY {day}");
    foreach (var account in s.LedgerBalances.Keys.OrderBy(x => x))
    {
        Console.WriteLine($"  {account}: closing={s.LedgerBalances[account]}, available={s.AvailableBalances[account]}, fees={s.Fees[account]:F2}");
        if (s.Authorizations.TryGetValue(account, out var auths))
            foreach (var auth in auths) Console.WriteLine($"    AUTH {auth}");
        Console.WriteLine($"    daily-interest-total={s.DailyInterest[account]}");
    }

    foreach (var error in s.Errors.Where(e => e.Day == day))
        Console.WriteLine($"  ERROR [{error.EventId}] {error.Message}");
}

Console.WriteLine();
Console.WriteLine("APPEND-ONLY ENTRIES");
foreach (var entry in ledger.Entries)
    Console.WriteLine($"  {entry.EventId,-18} D{entry.ValueDay} {entry.Type,-8} {entry.Money,-14} {entry.Reason}");

Console.WriteLine();
Console.WriteLine("REPLAY COMPLETE");
