using System.Globalization;

namespace AccountLedger;

public enum Currency { AED, BHD }
public enum EntryType { Credit, Debit, Fee, Interest }
public enum AuthorizationState { Approved, Declined, PartiallySettled, Settled }

public sealed record Money(Currency Currency, decimal Amount)
{
    public int Scale => Currency == Currency.AED ? 2 : 3;

    public Money Rounded() => this with
    {
        Amount = decimal.Round(Amount, Scale, MidpointRounding.AwayFromZero)
    };

    public override string ToString() =>
        $"{Amount.ToString($"F{Scale}", CultureInfo.InvariantCulture)} {Currency}";
}

public sealed record LedgerEntry(
    string EventId,
    string AccountId,
    EntryType Type,
    Money Money,
    int ValueDay,
    string Reason);

public sealed record Hold(
    string AuthorizationId,
    string AccountId,
    Money Amount,
    int CreatedDay);

public sealed record Authorization(
    string AuthorizationId,
    string AccountId,
    Money OriginalHold,
    Money RemainingHold,
    AuthorizationState State,
    int Day);

public sealed record LedgerEvent(
    string Id,
    int EventDay,
    string Kind,
    string AccountId,
    Currency Currency,
    decimal Amount,
    int ValueDay,
    string? AuthorizationId = null,
    string? ReferenceEventId = null,
    int InstallmentCount = 1);

public sealed record ReplayError(string EventId, int Day, string Message);

public sealed record DaySnapshot(
    int Day,
    IReadOnlyDictionary<string, Money> LedgerBalances,
    IReadOnlyDictionary<string, Money> AvailableBalances,
    IReadOnlyDictionary<string, decimal> Fees,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Authorizations,
    IReadOnlyList<ReplayError> Errors,
    IReadOnlyDictionary<string, decimal> DailyInterest);

public sealed class LedgerCore
{
    public const decimal OverdraftFeeAed = 25.00m;
    public const decimal DailyInterestRate = 0.0004m; // 0.04%

    private readonly Dictionary<string, (Currency Currency, decimal Opening)> _accounts = new();
    private readonly List<LedgerEntry> _entries = new();
    private readonly Dictionary<string, Hold> _holds = new();
    private readonly Dictionary<string, Authorization> _authorizations = new();
    private readonly HashSet<string> _feeKeys = new();
    private readonly Dictionary<string, decimal> _interestByAccount = new();
    private readonly List<ReplayError> _errors = new();

    public IReadOnlyList<LedgerEntry> Entries => _entries;
    public IReadOnlyList<ReplayError> Errors => _errors;

    public void AddAccount(string id, Currency currency, decimal opening)
    {
        _accounts.Add(id, (currency, decimal.Round(opening, currency == Currency.AED ? 2 : 3)));
    }

    public void Replay(IReadOnlyList<LedgerEvent> events, int days = 6)
    {
        foreach (var e in events)
            Apply(e);

        // Interest is calculated from each day's closing ledger balance after all
        // events known to the replay have been incorporated by value date.
        for (var day = 1; day <= days; day++)
        {
            foreach (var account in _accounts.Keys)
            {
                var balance = LedgerBalance(account, day);
                if (balance > 0)
                {
                    var currency = _accounts[account].Currency;
                    var interest = decimal.Round(balance * DailyInterestRate,
                        currency == Currency.AED ? 2 : 3, MidpointRounding.AwayFromZero);
                    _interestByAccount[account] = _interestByAccount.GetValueOrDefault(account) + interest;
                }
            }
        }

        // Capitalization happens once at Day 6. The daily rounded accruals are
        // already the exact amount to be capitalized.
        foreach (var (account, total) in _interestByAccount)
        {
            if (total == 0) continue;
            var currency = _accounts[account].Currency;
            var money = new Money(currency, total).Rounded();
            _entries.Add(new LedgerEntry(
                "INT-D6-" + account, account, EntryType.Interest, money, 6,
                "Capitalized daily interest"));
        }
    }

    private void Apply(LedgerEvent e)
    {
        if (!_accounts.TryGetValue(e.AccountId, out var account))
        {
            _errors.Add(new(e.Id, e.EventDay, "Unknown account."));
            return;
        }

        var scale = account.Currency == Currency.AED ? 2 : 3;
        var amount = decimal.Round(e.Amount, scale, MidpointRounding.AwayFromZero);

        switch (e.Kind)
        {
            case "CREDIT":
                if (e.InstallmentCount <= 1)
                {
                    AddEntry(e.Id, e.AccountId, EntryType.Credit, amount, e.ValueDay, "Credit");
                }
                else
                {
                    // Split by largest-remainder method so installments sum exactly.
                    var totalMinor = Convert.ToInt64(decimal.Round(amount * Pow10(scale), 0));
                    var baseMinor = totalMinor / e.InstallmentCount;
                    var remainder = totalMinor % e.InstallmentCount;
                    for (var i = 0; i < e.InstallmentCount; i++)
                    {
                        var minor = baseMinor + (i < remainder ? 1 : 0);
                        AddEntry($"{e.Id}-I{i + 1}", e.AccountId, EntryType.Credit,
                            minor / Pow10(scale), e.ValueDay, $"Installment {i + 1}/{e.InstallmentCount}");
                    }
                }
                break;

            case "DEBIT":
                AddEntry(e.Id, e.AccountId, EntryType.Debit, -amount, e.ValueDay, "Debit");
                AssessFeeIfNeeded(e.AccountId, e.ValueDay);
                break;

            case "AUTHORIZATION":
                Authorize(e, amount);
                break;

            case "SETTLEMENT":
                Settle(e);
                break;

            case "REVERSAL":
                Reverse(e);
                break;

            default:
                _errors.Add(new(e.Id, e.EventDay, $"Unknown event kind '{e.Kind}'."));
                break;
        }
    }

    private void Authorize(LedgerEvent e, decimal amount)
    {
        if (e.AuthorizationId is null)
        {
            _errors.Add(new(e.Id, e.EventDay, "Authorization ID is required."));
            return;
        }

        var available = AvailableBalance(e.AccountId, e.ValueDay);
        if (available - amount >= 0)
        {
            _holds[e.AuthorizationId] = new(e.AuthorizationId, e.AccountId,
                new Money(e.Currency, amount), e.EventDay);
            _authorizations[e.AuthorizationId] = new(e.AuthorizationId, e.AccountId,
                new Money(e.Currency, amount), new Money(e.Currency, amount),
                AuthorizationState.Approved, e.EventDay);
        }
        else
        {
            _authorizations[e.AuthorizationId] = new(e.AuthorizationId, e.AccountId,
                new Money(e.Currency, amount), new Money(e.Currency, 0),
                AuthorizationState.Declined, e.EventDay);
        }
    }

    private void Settle(LedgerEvent e)
    {
        if (e.AuthorizationId is null || !_authorizations.TryGetValue(e.AuthorizationId, out var auth))
        {
            _errors.Add(new(e.Id, e.EventDay,
                $"Settlement references unknown authorization '{e.AuthorizationId}'."));
            return;
        }

        if (auth.State == AuthorizationState.Declined)
        {
            _errors.Add(new(e.Id, e.EventDay, "Cannot settle a declined authorization."));
            return;
        }

        var amount = decimal.Round(e.Amount, auth.RemainingHold.Scale, MidpointRounding.AwayFromZero);
        if (amount > auth.RemainingHold.Amount)
        {
            _errors.Add(new(e.Id, e.EventDay, "Settlement exceeds remaining authorization hold."));
            return;
        }

        AddEntry(e.Id, e.AccountId, EntryType.Debit, -amount, e.ValueDay,
            $"Settlement of {e.AuthorizationId}");

        var remaining = auth.RemainingHold.Amount - amount;
        var state = remaining == 0 ? AuthorizationState.Settled : AuthorizationState.PartiallySettled;
        _authorizations[e.AuthorizationId] = auth with
        {
            RemainingHold = new Money(auth.RemainingHold.Currency, remaining),
            State = state
        };

        if (_holds.TryGetValue(e.AuthorizationId, out var hold))
            _holds[e.AuthorizationId] = hold with
            {
                Amount = new Money(hold.Amount.Currency, remaining)
            };

        AssessFeeIfNeeded(e.AccountId, e.ValueDay);
    }

    private void Reverse(LedgerEvent e)
    {
        if (e.ReferenceEventId is null)
        {
            _errors.Add(new(e.Id, e.EventDay, "Reversal requires a reference event."));
            return;
        }

        var original = _entries.FirstOrDefault(x => x.EventId == e.ReferenceEventId);
        if (original is null)
        {
            _errors.Add(new(e.Id, e.EventDay,
                $"Cannot reverse event '{e.ReferenceEventId}' because it is not present."));
            return;
        }

        // A reversal is a new append-only entry; the original remains untouched.
        AddEntry(e.Id, e.AccountId, EntryType.Credit,
            Math.Abs(original.Money.Amount), e.ValueDay, $"Reversal of {e.ReferenceEventId}");
    }

    private void AddEntry(string eventId, string accountId, EntryType type,
        decimal amount, int valueDay, string reason)
    {
        var currency = _accounts[accountId].Currency;
        var rounded = decimal.Round(amount, currency == Currency.AED ? 2 : 3,
            MidpointRounding.AwayFromZero);
        _entries.Add(new LedgerEntry(eventId, accountId, type,
            new Money(currency, rounded), valueDay, reason));
    }

    private void AssessFeeIfNeeded(string accountId, int day)
    {
        if (_accounts[accountId].Currency != Currency.AED) return;
        if (LedgerBalance(accountId, day) >= 0) return;

        var key = $"{accountId}:{day}";
        if (_feeKeys.Add(key))
            AddEntry($"FEE-{accountId}-D{day}", accountId, EntryType.Fee,
                -OverdraftFeeAed, day, "Daily overdraft fee");
    }

    public decimal LedgerBalance(string accountId, int throughDay)
    {
        var opening = _accounts[accountId].Opening;
        return opening + _entries
            .Where(x => x.AccountId == accountId && x.ValueDay <= throughDay)
            .Sum(x => x.Money.Amount);
    }

    public decimal AvailableBalance(string accountId, int throughDay)
    {
        var ledger = LedgerBalance(accountId, throughDay);
        var holds = _holds.Values
            .Where(h => h.AccountId == accountId && h.CreatedDay <= throughDay)
            .Sum(h => h.Amount.Amount);
        return ledger - holds;
    }

    public IReadOnlyDictionary<string, Authorization> Authorizations => _authorizations;

    public DaySnapshot Snapshot(int day)
    {
        var balances = _accounts.Keys.ToDictionary(
            a => a, a => new Money(_accounts[a].Currency, LedgerBalance(a, day)).Rounded());

        var available = _accounts.Keys.ToDictionary(
            a => a, a => new Money(_accounts[a].Currency, AvailableBalance(a, day)).Rounded());

        var fees = _accounts.Keys.ToDictionary(
            a => a, a => _entries.Where(x => x.AccountId == a && x.Type == EntryType.Fee &&
                                              x.ValueDay <= day)
                                  .Sum(x => Math.Abs(x.Money.Amount)));

        var auth = _authorizations.Values
            .GroupBy(a => a.AccountId)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<string>)g.Select(a => $"{a.AuthorizationId}={a.State} remaining={a.RemainingHold}").ToList());

        var interest = _accounts.Keys.ToDictionary(
            a => a, a => _interestByAccount.GetValueOrDefault(a));

        return new DaySnapshot(day, balances, available, fees, auth, _errors.ToList(), interest);
    }

    private static decimal Pow10(int scale) => scale switch { 2 => 100m, 3 => 1000m, _ => 1m };
}
