namespace Clam.Api.Tests.Features;

/// One test per endpoint that accepts a body, each sending a request in which
/// *every* validatable field is wrong at once.
///
/// The point is not to re-test rules the slice's own test file already covers
/// one at a time. It is to pin two things those tests cannot:
///
///  1. **Every failure comes back together.** FluentValidation's default cascade
///     is Continue, so one bad field must not hide the other four. A snapshot of
///     the whole `errors` array is the only assertion that notices if that ever
///     changes — a per-field test still passes when the response has been
///     truncated to the first failure.
///  2. **Validation runs before the handler.** Several of these use a route id
///     that does not exist, so a 400 here rather than a 404 is the proof that
///     nothing reached the database.
///
/// The bodies deliberately contain no unbindable values — no unknown enum
/// member, no string where a number belongs. Those fail in the *binder*, which
/// short-circuits before any validator runs and reports only the first problem;
/// <see cref="BindingFailureTests"/> covers that boundary separately. For the
/// same reason no public request DTO uses the `required` keyword: System.Text.Json
/// throws on the first missing one and the response would name that field alone.
public class EveryFieldInvalidTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Create_category_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/categories", new { name = "", color = "teal" });
        await Verify(response);
    }

    [Fact]
    public async Task Update_category_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/categories/nope", new { name = "", color = "teal" });
        await Verify(response);
    }

    [Fact]
    public async Task Create_note_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/notes", new { title = "", body = "orphaned", pinned = true });
        await Verify(response);
    }

    [Fact]
    public async Task Update_note_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/notes/nope", new { title = "" });
        await Verify(response);
    }

    [Fact]
    public async Task Create_tab_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs",
            new { person = "", description = "", amount = 0, direction = "IOwe" });
        await Verify(response);
    }

    [Fact]
    public async Task Update_tab_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/tabs/nope",
            new { person = "", description = "", amount = -5 });
        await Verify(response);
    }

    /// The only field this slice can reject — the rest are enums and bools the
    /// binder has already vetted.
    [Fact]
    public async Task Update_transaction_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/transactions/nope", new { categoryId = "" });
        await Verify(response);
    }

    [Fact]
    public async Task Create_investment_account_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/investments/accounts",
            new { name = "", category = "tulips", owner = "Alex" });
        await Verify(response);
    }

    [Fact]
    public async Task Update_investment_account_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/investments/accounts/nope",
            new { name = "", category = "tulips" });
        await Verify(response);
    }

    /// `date` is omitted rather than sent as a bad string: an unparseable date is
    /// a binder failure, whereas an absent one lands on `default(DateTime)` and
    /// reaches the validator, which is the rule under test.
    [Fact]
    public async Task Upsert_investment_snapshot_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Put("/api/investments/snapshots", new { accountId = "", value = 0 });
        await Verify(response);
    }

    [Fact]
    public async Task Set_recurring_verdict_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Put("/api/recurring/verdict",
            new { owner = "Joint", description = "", status = "Confirmed" });
        await Verify(response);
    }

    [Fact]
    public async Task Update_recurring_note_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/recurring/note",
            new { owner = "Joint", description = "", note = new string('x', 501) });
        await Verify(response);
    }

    /// One condition rather than none, because an empty list would satisfy the
    /// `When` guard on the all-exclusions rule and suppress it. A single negated
    /// condition with a blank value fails four rules at once.
    [Fact]
    public async Task Create_rule_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/rules", BadRule);
        await Verify(response);
    }

    [Fact]
    public async Task Update_rule_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Patch("/api/rules/nope", BadRule);
        await Verify(response);
    }

    /// Scope `rule` with a blank id *and* a draft body, so the discriminator's
    /// own rule and every child rule on the draft fail in one response.
    [Fact]
    public async Task Preview_rules_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/rules/preview", new { scope = "Rule", ruleId = "", rule = BadRule });
        await Verify(response);
    }

    /// Apply's two rules are mutually exclusive by construction — `ruleId` is
    /// only required when the scope is `rule`, and `draft` is rejected outright —
    /// so this is the one endpoint where a single request cannot fail everything.
    [Fact]
    public async Task Apply_rules_rejects_a_draft_scope()
    {
        await Given.NothingAsync();
        var response = await Post("/api/rules/apply", new { scope = "Draft" });
        await Verify(response);
    }

    [Fact]
    public async Task Apply_rules_rejects_a_rule_scope_with_no_id()
    {
        await Given.NothingAsync();
        var response = await Post("/api/rules/apply", new { scope = "Rule", ruleId = "" });
        await Verify(response);
    }

    /// Two blank ids, so the per-item rule reports both rather than stopping at
    /// the first. An empty list would fail the collection rule instead and never
    /// exercise the item rule.
    [Fact]
    public async Task Reorder_rules_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/rules/reorder", new { kind = "Category", ids = new[] { "", "" } });
        await Verify(response);
    }

    [Fact]
    public async Task Seed_data_reports_every_bad_field()
    {
        await Given.NothingAsync();
        var response = await Post("/api/dev/seed", new { count = 0 });
        await Verify(response);
    }

    /// A category rule with no category, scoped to a bank that does not exist,
    /// whose single condition is blank and negated. Shared by create, update and
    /// preview because all three bind the same <c>RuleInput</c>.
    private static object BadRule => new
    {
        kind = "Category",
        joinOperator = "AND",
        bank = "natwest",
        conditions = new[]
        {
            new { field = "Description", @operator = "Contains", value = "", negate = true },
        },
    };
}

/// The boundary the tests above stop at.
///
/// A value the binder cannot turn into the DTO's type never reaches a validator,
/// so the response names that one field and nothing else. These bodies are
/// written as literal JSON rather than serialised from an anonymous object,
/// because C#'s type system cannot produce most of them — which is exactly why
/// they are worth pinning: a hand-rolled call or a client one deploy behind sends
/// them, and the answer has to be a 400 that says which field, never a 500.
public class BindingFailureTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task An_unknown_enum_member_fails_in_the_binder_before_any_validator_runs()
    {
        await Given.NothingAsync();

        // `person` and `amount` are invalid too, but the binder gives up on
        // `direction` first and the validator never sees the request.
        var response = await Post("/api/tabs",
            new { person = "", description = "", amount = 0, direction = "Sideways" });

        await Verify(response);
    }

    [Fact]
    public async Task A_string_where_a_number_belongs_fails_in_the_binder()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs",
            """{"person":"Sam","description":"dinner","amount":"twenty","direction":"IOwe"}""");
        await Verify(response);
    }

    [Fact]
    public async Task An_explicit_null_on_a_non_nullable_fails_in_the_binder()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs",
            """{"person":"Sam","description":"dinner","amount":null,"direction":"IOwe"}""");
        await Verify(response);
    }

    [Fact]
    public async Task Malformed_json_fails_in_the_binder()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs", """{"person":"Sam",,}""");
        await Verify(response);
    }

    /// An empty body is not the same as `{}`: there is nothing for the
    /// deserialiser to read at all.
    [Fact]
    public async Task An_empty_body_fails_in_the_binder()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs", "");
        await Verify(response);
    }

    /// A number too large for the DTO's `decimal`. Distinct from a wrong type —
    /// the token is a number, it just does not fit.
    [Fact]
    public async Task A_number_too_large_for_the_target_type_fails_in_the_binder()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs",
            """{"person":"Sam","description":"dinner","amount":1e40,"direction":"IOwe"}""");
        await Verify(response);
    }

    /// `{}` binds cleanly — every property lands on its default — so this one
    /// reaches the validator and comes back with the full error list, not a
    /// binder complaint. The contrast with the cases above is the point.
    [Fact]
    public async Task An_empty_object_binds_and_is_rejected_by_the_validator_instead()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs", "{}");
        await Verify(response);
    }

    /// A field the DTO does not declare is ignored rather than rejected, so this
    /// is a 201. Worth pinning: the opposite choice would break every client that
    /// posts back a response body it just received.
    [Fact]
    public async Task An_unknown_property_is_ignored_rather_than_rejected()
    {
        await Given.NothingAsync();
        var response = await PostRaw("/api/tabs",
            """{"person":"Sam","description":"dinner","amount":5,"direction":"IOwe","nonsense":true}""");
        await Verify(response);
    }

    /// The `note` field on a transaction is bound as a raw JsonElement so that an
    /// explicit null can be told from an absent key. That means it accepts *any*
    /// JSON shape, including an object — which the column cannot store.
    [Fact]
    public async Task A_json_object_where_a_note_string_belongs_is_rejected()
    {
        await Given.SeededAsync();
        var response = await PatchRaw($"/api/transactions/{Arrange.RentTransactionId}",
            """{"note":{"nested":"object"}}""");
        await Verify(response);
    }
}
