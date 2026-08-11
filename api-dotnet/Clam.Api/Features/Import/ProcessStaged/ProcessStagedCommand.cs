using System.Data;
using Clam.Api.Domain;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Fx;
using Dapper;

namespace Clam.Api.Features.Import.ProcessStaged;

/// The stage → process half of the import pipeline: read every pending staged
/// row, normalise it into a Transaction, and mark the staged row done.
///
/// One class rather than one per bank, because the banks are variations on a
/// single pipeline and the interesting content is where they differ. Splitting
/// them would hide that behind seven files of identical scaffolding.
public sealed class ProcessStagedCommand(IDbConnectionFactory factory, IIdGenerator ids, IFxRateService fx)
{
    private const string Uncategorised = "Uncategorised";
    private const string UsdCurrency = "USD";
    private const string GbpCurrency = "GBP";

    /// Insert only if nothing already claims this external id. A guard in the
    /// statement, not a read-then-write, because two runs overlapping is exactly
    /// when the check-then-act version breaks.
    private const string InsertSql = """
        INSERT INTO [Transactions]
            ([id], [description], [amount], [type], [date], [categoryId], [bucket],
             [externalId], [owner], [statementFileId], [originalAmount], [originalCurrency])
        SELECT @Id, @Description, @Amount, @Type, @Date, @CategoryId, @Bucket,
               @ExternalId, @Owner, @StatementFileId, @OriginalAmount, @OriginalCurrency
        WHERE NOT EXISTS (SELECT 1 FROM [Transactions] WHERE [externalId] = @ExternalId);
        """;

    private const string UncategorisedIdSql = "SELECT [id] FROM [Categories] WHERE [name] = @Name;";

    public async Task<ProcessStagedResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);

        var rules = await RuleStore.LoadRulesAsync(connection, ct);
        var categoryNameById = await RuleStore.LoadCategoryNamesAsync(connection, ct);

        var uncategorisedId = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(UncategorisedIdSql, new { Name = Uncategorised }, cancellationToken: ct))
            ?? throw new InvalidOperationException(
                $"The {Uncategorised} category is missing; the import pipeline has nothing to fall back to.");

        var run = new ProcessRun(connection, ids, rules, categoryNameById, uncategorisedId, fx);

        await run.ProcessMonzoAsync(ct);
        await run.ProcessAmexAsync(ct);
        await run.ProcessBarclaysAsync(ct);
        await run.ProcessSantanderAsync(ct);
        await run.ProcessHsbcAsync(ct);
        await run.ProcessSofiAsync(ct);
        await run.ProcessChaseAsync(ct);

        return run.Tally;
    }

    /// One run's mutable state, kept off the command itself so the command stays
    /// safe to register as a scoped service and a run cannot leak counts into the
    /// next one.
    private sealed class ProcessRun(
        IDbConnection connection,
        IIdGenerator ids,
        // Qualified: System.Data also has a `Rule`, and this file needs IDbConnection.
        IReadOnlyList<Domain.Rules.Rule> rules,
        IReadOnlyDictionary<string, string> categoryNameById,
        string uncategorisedId,
        IFxRateService fx)
    {
        internal ProcessStagedResponse Tally { get; } = new();

        // ── Monzo ──────────────────────────────────────────────────────────
        //
        // The retail (debit) account's id is the "primary" one stored on the
        // credential. Anything else synced — currently just Flex — is treated as
        // its own bank so it gets its own externalId namespace and rule scope.
        internal async Task ProcessMonzoAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT  [id], [monzoId], [created], [amountPence], [description],
                        [monzoCategory], [merchantName], [accountId]
                FROM    [MonzoApiTransactions]
                WHERE   [status] = 'pending';

                SELECT TOP 1 [accountId] FROM [MonzoCredentials];
                """;

            using var grid = await connection.QueryMultipleAsync(
                new CommandDefinition(Sql, cancellationToken: ct));

            var rows = (await grid.ReadAsync<MonzoRow>()).AsList();
            var primaryAccountId = await grid.ReadSingleOrDefaultAsync<string?>();

            foreach (var row in rows)
            {
                var isFlex = !string.IsNullOrEmpty(primaryAccountId)
                    && !string.Equals(row.AccountId, primaryAccountId, StringComparison.Ordinal);
                var bank = isFlex ? "flex" : "monzo";

                var type = row.AmountPence >= 0 ? TransactionType.Income : TransactionType.Expense;
                var name = row.MerchantName ?? row.Description;
                var (categoryId, bucket) = Classify(bank, name, type);

                await InsertAsync(new NormalisedTransaction
                {
                    ExternalId = $"{bank}:{row.MonzoId}",
                    Description = name,
                    Amount = Math.Abs(row.AmountPence) / 100m,
                    Type = type,
                    Date = row.Created,
                    CategoryId = categoryId,
                    Bucket = bucket,
                    Owner = ImportOwner.Resolve(row.MonzoCategory, name, "Alex"),
                }, ct);

                await MarkAsync("MonzoApiTransactions", "id", row.Id, StagedStatus.Processed, ct);
                Tally.Processed++;
            }
        }

        // ── Amex ───────────────────────────────────────────────────────────
        internal async Task ProcessAmexAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT  [transactionId], [transactionDate], [description], [amount],
                        [isCredit], [owner], [statementFileId]
                FROM    [AmexTransactions]
                WHERE   [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<AmexRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                var amount = StagedAmount.Parse(row.Amount);
                var date = StagedAmount.ParseDate(row.TransactionDate);

                if (amount is null || date is null)
                {
                    await MarkAsync("AmexTransactions", "transactionId", row.TransactionId, StagedStatus.Errored, ct);
                    Tally.Errored++;
                    continue;
                }

                var type = row.IsCredit ? TransactionType.Income : TransactionType.Expense;
                var (categoryId, bucket) = Classify("amex", row.Description, type);

                await InsertAsync(new NormalisedTransaction
                {
                    ExternalId = $"amex:{row.TransactionId}",
                    Description = row.Description,
                    Amount = amount.Value,
                    Type = type,
                    Date = date.Value,
                    CategoryId = categoryId,
                    Bucket = bucket,
                    Owner = row.Owner,
                    StatementFileId = row.StatementFileId,
                }, ct);

                await MarkAsync("AmexTransactions", "transactionId", row.TransactionId, StagedStatus.Processed, ct);
                Tally.Processed++;
            }
        }

        // ── Barclays ───────────────────────────────────────────────────────
        //
        // Credits on this card are payments *to* it from an account we already
        // import, so booking them would double-count the money.
        internal async Task ProcessBarclaysAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT [id], [transactionId], [date], [description], [amount], [isCredit], [owner]
                FROM   [BarclaysTransactions]
                WHERE  [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<BarclaysRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                if (row.IsCredit)
                {
                    await MarkAsync("BarclaysTransactions", "id", row.Id, StagedStatus.Skipped, ct);
                    Tally.Skipped++;
                    continue;
                }

                var amount = StagedAmount.Parse(row.Amount);
                var date = StagedAmount.ParseDate(row.Date);

                if (amount is null || date is null)
                {
                    await MarkAsync("BarclaysTransactions", "id", row.Id, StagedStatus.Errored, ct);
                    Tally.Errored++;
                    continue;
                }

                var (categoryId, bucket) = Classify("barclays", row.Description, TransactionType.Expense);

                await InsertAsync(new NormalisedTransaction
                {
                    // Barclays statements carry no usable per-row identifier, so
                    // the surrogate key stands in when the parser could not derive one.
                    ExternalId = $"barclays:{row.TransactionId ?? row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    Description = row.Description,
                    Amount = amount.Value,
                    Type = TransactionType.Expense,
                    Date = date.Value,
                    CategoryId = categoryId,
                    Bucket = bucket,
                    Owner = row.Owner,
                }, ct);

                await MarkAsync("BarclaysTransactions", "id", row.Id, StagedStatus.Processed, ct);
                Tally.Processed++;
            }
        }

        // ── Santander ──────────────────────────────────────────────────────
        internal async Task ProcessSantanderAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT [id], [transactionId], [date], [description], [moneyIn], [moneyOut], [owner]
                FROM   [SantanderTransactions]
                WHERE  [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<SantanderRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                // Which column is populated *is* the direction — the statement
                // has two money columns, not one signed one.
                var isIncome = row.MoneyIn is not null;
                var amount = StagedAmount.Parse(row.MoneyIn ?? row.MoneyOut);
                var date = StagedAmount.ParseDate(row.Date);

                var status = await ProcessTwoColumnRowAsync(
                    "santander", $"santander:{row.TransactionId ?? row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    row.Description, amount, date, isIncome, row.Owner, ct);

                await MarkAsync("SantanderTransactions", "id", row.Id, status, ct);
            }
        }

        // ── HSBC ───────────────────────────────────────────────────────────
        internal async Task ProcessHsbcAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT [id], [transactionId], [date], [description], [moneyIn], [moneyOut], [owner]
                FROM   [HsbcTransactions]
                WHERE  [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<HsbcRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                var isIncome = row.MoneyIn is not null;
                var amount = StagedAmount.Parse(row.MoneyIn ?? row.MoneyOut);
                var date = StagedAmount.ParseDate(row.Date);

                var status = await ProcessTwoColumnRowAsync(
                    "hsbc", $"hsbc:{row.TransactionId}",
                    row.Description, amount, date, isIncome, row.Owner, ct);

                await MarkAsync("HsbcTransactions", "id", row.Id, status, ct);
            }
        }

        // ── SoFi (USD → GBP) ───────────────────────────────────────────────
        internal async Task ProcessSofiAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT [id], [transactionId], [date], [description], [amount], [isCredit], [owner]
                FROM   [SofiTransactions]
                WHERE  [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<SofiRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                if (StagedAmount.IsInternalTransfer(row.Description))
                {
                    await MarkAsync("SofiTransactions", "id", row.Id, StagedStatus.Skipped, ct);
                    Tally.Skipped++;
                    continue;
                }

                var status = await ProcessUsdRowAsync(
                    "sofi", $"sofi:{row.TransactionId}", row.Description, row.Amount,
                    row.Date, row.IsCredit, row.Owner, ct);

                await MarkAsync("SofiTransactions", "id", row.Id, status, ct);
            }
        }

        // ── Chase (USD → GBP) ──────────────────────────────────────────────
        internal async Task ProcessChaseAsync(CancellationToken ct)
        {
            const string Sql = """
                SELECT [id], [transactionId], [date], [description], [amount], [isCredit], [owner]
                FROM   [ChaseTransactions]
                WHERE  [status] = 'pending';
                """;

            var rows = await connection.QueryAsync<ChaseRow>(new CommandDefinition(Sql, cancellationToken: ct));

            foreach (var row in rows)
            {
                var status = await ProcessUsdRowAsync(
                    "chase", $"chase:{row.TransactionId}", row.Description, row.Amount,
                    row.Date, row.IsCredit, row.Owner, ct);

                await MarkAsync("ChaseTransactions", "id", row.Id, status, ct);
            }
        }

        /// Santander and HSBC are the same shape: two money columns, a string
        /// date, and a zero row that means "statement filler, not a payment".
        private async Task<string> ProcessTwoColumnRowAsync(
            string bank, string externalId, string description,
            decimal? amount, DateTime? date, bool isIncome, string owner, CancellationToken ct)
        {
            // A row with an unparseable amount or date is marked errored and
            // skipped — one bad row must never abort the whole run.
            if (amount is null || date is null)
            {
                Tally.Errored++;
                return StagedStatus.Errored;
            }

            if (amount.Value == 0)
            {
                Tally.Skipped++;
                return StagedStatus.Skipped;
            }

            var type = isIncome ? TransactionType.Income : TransactionType.Expense;
            var (categoryId, bucket) = Classify(bank, description, type);

            await InsertAsync(new NormalisedTransaction
            {
                ExternalId = externalId,
                Description = description,
                Amount = amount.Value,
                Type = type,
                Date = date.Value,
                CategoryId = categoryId,
                Bucket = bucket,
                Owner = owner,
            }, ct);

            Tally.Processed++;
            return StagedStatus.Processed;
        }

        /// The two US banks: identical but for the namespace, and both need the
        /// amount converted at the transaction's own date before it can be
        /// totalled against anything sterling.
        private async Task<string> ProcessUsdRowAsync(
            string bank, string externalId, string description, string rawAmount,
            string rawDate, bool isCredit, string owner, CancellationToken ct)
        {
            var usd = StagedAmount.Parse(rawAmount);
            var date = StagedAmount.ParseDate(rawDate);

            if (usd is null || date is null)
            {
                Tally.Errored++;
                return StagedStatus.Errored;
            }

            if (usd.Value == 0)
            {
                Tally.Skipped++;
                return StagedStatus.Skipped;
            }

            // Checked before spending an FX lookup on a row that is already
            // imported — a re-run of /process would otherwise hit Frankfurter
            // once per existing row.
            if (await ExistsAsync(externalId, ct))
            {
                Tally.Processed++;
                return StagedStatus.Processed;
            }

            var type = isCredit ? TransactionType.Income : TransactionType.Expense;
            var (categoryId, bucket) = Classify(bank, description, type);
            var converted = await fx.ConvertWithFallbackAsync(usd.Value, UsdCurrency, GbpCurrency, date.Value, externalId, ct);

            await InsertAsync(new NormalisedTransaction
            {
                ExternalId = externalId,
                Description = description,
                Amount = converted.Amount,
                Type = type,
                Date = date.Value,
                CategoryId = categoryId,
                Bucket = bucket,
                Owner = owner,
                OriginalAmount = usd.Value,
                OriginalCurrency = UsdCurrency,
            }, ct);

            Tally.Processed++;
            return StagedStatus.Processed;
        }

        /// The two-pass pipeline the Rules page dry-runs: Category rules resolve
        /// first, then the resulting category *name* is fed to the Bucket pass —
        /// which is what lets "category = Transport AND description contains
        /// uber → Wants" work.
        ///
        /// Buckets are assigned to income as well as expenses: a Bucket total is
        /// a signed net, so a refund landing in Wants subtracts from it.
        private (string CategoryId, Bucket? Bucket) Classify(string bank, string description, TransactionType type)
        {
            var basis = new MatchableTransaction(description, type, null, bank);

            var categoryRules = rules.Where(r => r.Kind == RuleKind.Category).ToList();
            var categoryId = RuleEngine.ResolveRule(basis, categoryRules)?.CategoryId ?? uncategorisedId;

            var bucketRules = rules.Where(r => r.Kind == RuleKind.Bucket).ToList();
            var withCategory = basis with { CategoryName = categoryNameById.GetValueOrDefault(categoryId) };

            return (categoryId, RuleEngine.ResolveRule(withCategory, bucketRules)?.Bucket);
        }

        private Task<bool> ExistsAsync(string externalId, CancellationToken ct) =>
            connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CAST(COUNT(*) AS BIT) FROM [Transactions] WHERE [externalId] = @ExternalId;",
                new { ExternalId = externalId },
                cancellationToken: ct));

        private Task<int> InsertAsync(NormalisedTransaction tx, CancellationToken ct) =>
            connection.ExecuteAsync(new CommandDefinition(InsertSql, new
            {
                Id = ids.NewId(),
                tx.Description,
                tx.Amount,
                Type = tx.Type.ToString(),
                tx.Date,
                tx.CategoryId,
                Bucket = tx.Bucket?.ToString(),
                tx.ExternalId,
                tx.Owner,
                tx.StatementFileId,
                tx.OriginalAmount,
                tx.OriginalCurrency,
            }, cancellationToken: ct));

        /// The table and key column are compile-time literals from the call
        /// sites above, never anything a caller supplied.
        private Task<int> MarkAsync(string table, string keyColumn, object key, string status, CancellationToken ct) =>
            connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE [{table}] SET [status] = @Status WHERE [{keyColumn}] = @Key;",
                new { Status = status, Key = key },
                cancellationToken: ct));
    }

    private sealed class MonzoRow
    {
        public string Id { get; set; } = "";
        public string MonzoId { get; set; } = "";
        public DateTime Created { get; set; }
        public int AmountPence { get; set; }
        public string Description { get; set; } = "";
        public string MonzoCategory { get; set; } = "";
        public string? MerchantName { get; set; }
        public string AccountId { get; set; } = "";
    }

    private sealed class AmexRow
    {
        public string TransactionId { get; set; } = "";
        public string TransactionDate { get; set; } = "";
        public string Description { get; set; } = "";
        public string Amount { get; set; } = "";
        public bool IsCredit { get; set; }
        public string Owner { get; set; } = "";
        public string? StatementFileId { get; set; }
    }

    private sealed class BarclaysRow
    {
        public int Id { get; set; }
        public string? TransactionId { get; set; }
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        public string Amount { get; set; } = "";
        public bool IsCredit { get; set; }
        public string Owner { get; set; } = "";
    }

    private sealed class SantanderRow
    {
        public int Id { get; set; }
        public string? TransactionId { get; set; }
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        public string? MoneyIn { get; set; }
        public string? MoneyOut { get; set; }
        public string Owner { get; set; } = "";
    }

    private sealed class HsbcRow
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        public string? MoneyIn { get; set; }
        public string? MoneyOut { get; set; }
        public string Owner { get; set; } = "";
    }

    private sealed class SofiRow
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        public string Amount { get; set; } = "";
        public bool IsCredit { get; set; }
        public string Owner { get; set; } = "";
    }

    private sealed class ChaseRow
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
        public string Amount { get; set; } = "";
        public bool IsCredit { get; set; }
        public string Owner { get; set; } = "";
    }
}
