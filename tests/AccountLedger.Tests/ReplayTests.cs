using AccountLedger;

namespace AccountLedger.Tests;

public class ReplayTests
{
    private static LedgerCore BuildAndReplay()
    {
        var ledger = new LedgerCore();
        ledger.AddAccount("ACC-001", Currency.AED, 0.00m);
        ledger.AddAccount("ACC-002", Currency.BHD, 0.000m);

        ledger.Replay(new List<LedgerEvent>
        {
            new("E1", 1, "CREDIT", "ACC-001", Currency.AED, 1200m, 1),
            new("E2", 1, "DEBIT", "ACC-001", Currency.AED, 950m, 1),
            new("E3", 2, "AUTHORIZATION", "ACC-001", Currency.AED, 200m, 2, "Auth-A"),
            new("E4", 3, "CREDIT", "ACC-001", Currency.AED, 400m, 3),
            new("E5", 4, "SETTLEMENT", "ACC-001", Currency.AED, 185m, 4, "Auth-A"),
            new("E6", 4, "SETTLEMENT", "ACC-001", Currency.AED, 180m, 4, "Auth-Z"),
            new("E7", 5, "DEBIT", "ACC-001", Currency.AED, 620m, 2),
            new("E8", 5, "AUTHORIZATION", "ACC-001", Currency.AED, 90m, 5, "Auth-B"),
            new("E9", 6, "REVERSAL", "ACC-001", Currency.AED, 0m, 2, null, "E7"),
            new("E10", 5, "CREDIT", "ACC-002", Currency.BHD, 10m, 5, null, null, 3)
        });
        return ledger;
    }

    [Fact]
    public void Day2_BackValued_E7_Is_Minus_370_Before_Fee()
    {
        var l = BuildAndReplay();
        // E7 is a Day-2 value-dated debit. The raw pre-fee balance is:
        // 1200 - 950 - 620 = -370.
        Assert.Equal(-370m, l.Entries.Where(x => x.EventId != "FEE-ACC-001-D2")
            .Where(x => x.AccountId == "ACC-001" && x.ValueDay <= 2)
            .Sum(x => x.Money.Amount));
    }

    [Fact]
    public void E7_Assesses_Exactly_One_Day2_Fee()
    {
        var l = BuildAndReplay();
        var fees = l.Entries.Where(x => x.EventId == "FEE-ACC-001-D2").ToList();
        Assert.Single(fees);
        Assert.Equal(-25.00m, fees[0].Money.Amount);
    }

    [Fact]
    public void AuthA_Is_Partially_Settled()
    {
        var l = BuildAndReplay();
        Assert.Equal(AuthorizationState.PartiallySettled, l.Authorizations["Auth-A"].State);
        Assert.Equal(15m, l.Authorizations["Auth-A"].RemainingHold.Amount);
    }

    [Fact]
    public void Unknown_Auth_Settlement_Is_Rejected()
    {
        var l = BuildAndReplay();
        Assert.Contains(l.Errors, e => e.EventId == "E6");
        Assert.DoesNotContain(l.Entries, e => e.EventId == "E6");
    }

    [Fact]
    public void AuthB_Is_Declined_And_Does_Not_Change_Ledger_Balance()
    {
        var l = BuildAndReplay();
        Assert.Equal(AuthorizationState.Declined, l.Authorizations["Auth-B"].State);
        Assert.Equal(0m, l.Authorizations["Auth-B"].RemainingHold.Amount);
        Assert.Equal(440m, l.LedgerBalance("ACC-001", 5));
    }

    [Fact]
    public void Reversal_Is_Append_Only_And_Does_Not_Delete_E7()
    {
        var l = BuildAndReplay();
        Assert.Contains(l.Entries, x => x.EventId == "E7");
        Assert.Contains(l.Entries, x => x.EventId == "E9");
        Assert.Equal(0m, l.LedgerBalance("ACC-001", 2)); // -370 - 25 fee + 620 reversal
    }

    [Fact]
    public void Bhd_Installments_Sum_Exactly_To_Ten()
    {
        var l = BuildAndReplay();
        var installments = l.Entries.Where(x => x.EventId.StartsWith("E10-I")).ToList();
        Assert.Equal(3, installments.Count);
        Assert.Equal(new[] { 3.334m, 3.333m, 3.333m }, installments.Select(x => x.Money.Amount));
        Assert.Equal(10.000m, installments.Sum(x => x.Money.Amount));
    }

    [Fact]
    public void Intentionally_Failing_Requirement_Demonstrates_Bhd_Equal_Split_Conflict()
    {
        var l = BuildAndReplay();
        var installments = l.Entries.Where(x => x.EventId.StartsWith("E10-I")).ToList();

        // INTENTIONAL FAILURE:
        // The rejected acceptance criterion demands 3 x 3.334 = 10.002 BHD.
        // That cannot represent the booked 10.000 BHD without inventing 0.002 BHD.
        Assert.Equal(new[] { 3.334m, 3.334m, 3.334m },
            installments.Select(x => x.Money.Amount));
    }
}
