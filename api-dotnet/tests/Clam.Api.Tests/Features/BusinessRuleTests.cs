namespace Clam.Api.Tests.Features;

/// The rules that are about *meaning* rather than about fitting.
///
/// <see cref="LengthLimitTests"/> covers the other kind: a string one character
/// wider than its column, where the only question is whether the validator and
/// db/schema.sql agree on a number. Every case here is instead a value that binds
/// cleanly, fits every column it touches, and is still wrong — a snapshot dated
/// next year, a settlement in the future, a rule that lists the same id twice.
/// Left unchecked none of them is a 500; they are accepted, stored, and read back
/// later as fact, which is why they need tests that fail loudly now.
///
/// Two things shape how these are written:
///
///  1. **The tripping value is spelled out.** `1_000_000.01m` and `10.999m` say
///     which side of which boundary they are on; `Looong` spells the same thing
///     for a string. A reader should be able to see what the rule is from the
///     request alone.
///  2. **Each boundary is tested from both sides where the arithmetic is
///     subtle.** A `MaximumLength` that is one too strict passes every rejection
///     test ever written, so the accepting half is the only thing that catches
///     it. Rules whose boundary is a plain count — 21 conditions against a limit
///     of 20 — get the rejection alone.
public class BusinessRuleTests(ClamApiFactory api) : ApiTest(api)
{
    // ─── Rules ───────────────────────────────────────────────────────────────

    /// `[a, a]` against the rules `{a, b}` passes the command's own check: the
    /// count matches and every id exists. The reorder then writes two positions
    /// for `a` and none for `b`, silently changing precedence for a rule the user
    /// never touched. A 400 here — with no rules in the database at all — is also
    /// the proof that the check happens before anything is read.
    [Fact]
    public async Task Reorder_rules_rejects_the_same_rule_listed_twice()
    {
        await Given.NothingAsync();

        var response = await Post("/api/rules/reorder", new
        {
            kind = "Category",
            ids = new[] { "crule0000000000000001cat", "crule0000000000000001cat" },
        });

        await Verify(response);
    }

    /// 31 characters against an NVARCHAR(30) id column. Not a length a client
    /// reaches by accident, but the value is INSERTed rather than compared, so
    /// without the rule it is a truncation 500 instead of "no such category".
    [Fact]
    public async Task Create_rule_rejects_a_category_id_longer_than_an_id_column()
    {
        await Given.SeededAsync();

        var response = await Post("/api/rules", RuleWith(TooLongId));

        await Verify(response);
    }

    /// One past the ceiling. Every condition is a stored row re-evaluated against
    /// every transaction on every import, so the count is the cost.
    [Fact]
    public async Task Create_rule_rejects_more_conditions_than_a_rule_may_have()
    {
        await Given.SeededAsync();

        var response = await Post("/api/rules", RuleWith(Arrange.RentCategoryId, conditions: 21));

        await Verify(response);
    }

    // ─── Tabs ────────────────────────────────────────────────────────────────

    /// A penny past the ceiling. The column is DECIMAL(18, 2), so a decimal well
    /// past this still binds and then dies as an arithmetic overflow on the
    /// INSERT — the bound is what turns that 500 into a field error.
    [Fact]
    public async Task Create_tab_rejects_an_amount_over_the_ceiling()
    {
        await Given.NothingAsync();

        var response = await Post("/api/tabs", new
        {
            person = "Sam",
            description = "dinner",
            amount = 1_000_000.01m,
            direction = "IOwe",
        });

        await Verify(response);
    }

    /// The column keeps two decimal places, so this is not stored as sent — it is
    /// rounded to 11.00 and echoed back as though 10.999 had been accepted.
    [Fact]
    public async Task Create_tab_rejects_an_amount_with_more_precision_than_the_column_keeps()
    {
        await Given.NothingAsync();

        var response = await Post("/api/tabs", new
        {
            person = "Sam",
            description = "dinner",
            amount = 10.999m,
            direction = "IOwe",
        });

        await Verify(response);
    }

    /// The other side of both boundaries at once: exactly the ceiling, and
    /// exactly two decimal places.
    [Fact]
    public async Task A_tab_amount_of_exactly_the_ceiling_and_scale_is_accepted()
    {
        await Given.NothingAsync();

        var response = await Post("/api/tabs", new
        {
            person = "Sam",
            description = "the whole holiday",
            amount = 1_000_000.00m,
            direction = "TheyOwe",
        });

        await Verify(response);
    }

    /// The frozen clock is 2026-08-11T12:00:00Z, so this is half a day ahead.
    /// Backdating a settlement is the point of the field; forward-dating one
    /// records a tab as settled on a day that has not happened.
    [Fact]
    public async Task Update_tab_rejects_a_settlement_dated_in_the_future()
    {
        await Given.TabsAsync();

        var response = await Patch($"/api/tabs/{Arrange.OpenTabId}",
            new { status = "Settled", settledAt = "2026-08-12T00:00:00Z" });

        await Verify(response);
    }

    /// The command gives an explicit `settledAt` priority over the status, so
    /// without this rule the same request reopens the tab *and* stamps it settled.
    [Fact]
    public async Task Update_tab_rejects_a_settled_date_on_a_tab_it_is_reopening()
    {
        await Given.TabsAsync();

        var response = await Patch($"/api/tabs/{Arrange.SettledTabId}",
            new { status = "Open", settledAt = "2026-08-01T00:00:00Z" });

        await Verify(response);
    }

    // ─── Investments ─────────────────────────────────────────────────────────

    /// A day past the frozen clock. A future snapshot is not merely odd: it is
    /// the newest row, so it wins every "latest value" read and is reported as
    /// the current holding.
    [Fact]
    public async Task Upsert_investment_snapshot_rejects_a_date_in_the_future()
    {
        await Given.InvestmentHistoryAsync();

        var response = await Put("/api/investments/snapshots",
            new { accountId = EquityAccountId, date = "2026-08-12T00:00:00Z", value = 1200 });

        await Verify(response);
    }

    /// One day under the floor. Catches a two-digit year or a unix epoch that
    /// survived a client-side date parse, rather than policing history.
    [Fact]
    public async Task Upsert_investment_snapshot_rejects_a_date_before_the_app_could_have_history()
    {
        await Given.InvestmentHistoryAsync();

        var response = await Put("/api/investments/snapshots",
            new { accountId = EquityAccountId, date = "1999-12-31T00:00:00Z", value = 1200 });

        await Verify(response);
    }

    /// One pound past the ceiling. The column is FLOAT, so without a bound this
    /// is stored happily and every chart it appears in is a flat line and a spike.
    [Fact]
    public async Task Upsert_investment_snapshot_rejects_a_value_past_the_ceiling()
    {
        await Given.InvestmentHistoryAsync();

        var response = await Put("/api/investments/snapshots",
            new { accountId = EquityAccountId, date = "2026-03-31T00:00:00Z", value = 100_000_001 });

        await Verify(response);
    }

    /// 31 characters against an NVARCHAR(30) id column. The MERGE INSERTs this,
    /// so without the rule it is a truncation 500 rather than the 404 the
    /// foreign-key catch exists to produce.
    [Fact]
    public async Task Upsert_investment_snapshot_rejects_an_account_id_longer_than_an_id_column()
    {
        await Given.InvestmentHistoryAsync();

        var response = await Put("/api/investments/snapshots",
            new { accountId = TooLongId, date = "2026-03-31T00:00:00Z", value = 1200 });

        await Verify(response);
    }

    /// The other side of the date boundary: exactly the frozen clock, to the
    /// second. `LessThan` instead of `LessThanOrEqualTo` would reject a snapshot
    /// taken this instant and no rejection test would notice.
    [Fact]
    public async Task A_snapshot_dated_exactly_now_is_accepted()
    {
        await Given.InvestmentHistoryAsync();

        var response = await Put("/api/investments/snapshots",
            new { accountId = EquityAccountId, date = "2026-08-11T12:00:00Z", value = 1300 });

        await Verify(response);
    }

    /// A percentage. 450 is the shape a fat-fingered 4.50 takes, and it is then
    /// projected forward as a growth rate.
    [Fact]
    public async Task Create_investment_account_rejects_a_rate_outside_the_plausible_range()
    {
        await Given.NothingAsync();

        var response = await Post("/api/investments/accounts",
            new { name = "Chase Saver", category = "cash", owner = "Alex", rate = 450 });

        await Verify(response);
    }

    // ─── Users ───────────────────────────────────────────────────────────────

    /// 129 characters. Not a column width — the password is hashed before it is
    /// stored — but a bound on work: scrypt is deliberately expensive, and this
    /// is the one endpoint where an unauthenticated caller chooses how much of it
    /// to ask for.
    [Fact]
    public async Task Create_user_rejects_a_password_longer_than_the_hasher_should_be_asked_for()
    {
        await Given.NothingAsync();

        var response = await Post("/api/admin/users", new
        {
            name = "Sam Reed",
            email = "sam@example.com",
            password = Looong(129),
        });

        await Verify(response);
    }

    [Fact]
    public async Task A_password_of_exactly_the_maximum_length_is_accepted()
    {
        await Given.NothingAsync();

        var response = await Post("/api/admin/users", new
        {
            name = "Sam Reed",
            email = "sam@example.com",
            password = Looong(128),
        });

        await Verify(response);
    }

    // ─── Categories ──────────────────────────────────────────────────────────

    /// The command stores the name trimmed, so the validator measures it trimmed.
    /// 41 characters of padding is still 41 characters of name.
    [Fact]
    public async Task Create_category_rejects_a_name_that_is_too_long_even_after_trimming()
    {
        await Given.NothingAsync();

        var response = await Post("/api/categories",
            new { name = "   " + Looong(41) + "   ", color = "#14b8a6" });

        await Verify(response);
    }

    /// The half that measuring the raw string gets wrong: 40 characters of name
    /// inside 46 characters of JSON. What is stored is what was checked.
    [Fact]
    public async Task Create_category_accepts_a_name_that_fits_once_trimmed()
    {
        await Given.NothingAsync();

        var response = await Post("/api/categories",
            new { name = "   " + Looong(40) + "   ", color = "#14b8a6" });

        await Verify(response);
    }

    // ─── Values ──────────────────────────────────────────────────────────────

    /// 31 characters — one past every id column in db/schema.sql, and spelled so
    /// that is visible without counting.
    private const string TooLongId = "cthisidiswaytoolongforacolumn31";

    private const string EquityAccountId = "cacct0000000000001equity";

    /// A too-long string that says so: `LOOO…OONG` of exactly <paramref name="length"/>
    /// characters. `new string('x', 129)` pins the same boundary but a reader has
    /// to take the number on trust; this one reads as what it is.
    private static string Looong(int length) => "L" + new string('O', length - 3) + "NG";

    /// A minimal valid Category rule, varied only where a test needs it. Written
    /// as a helper rather than a shared constant because two of the three cases
    /// differ in a *count*, which a constant cannot express.
    private static object RuleWith(string categoryId, int conditions = 1) => new
    {
        kind = "Category",
        joinOperator = "AND",
        categoryId,
        conditions = Enumerable.Range(0, conditions)
            .Select(i => new
            {
                field = "Description",
                @operator = "Contains",
                value = $"MERCHANT {i}",
                negate = false,
            })
            .ToArray(),
    };
}
