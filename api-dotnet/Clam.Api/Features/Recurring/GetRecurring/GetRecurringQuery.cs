using Clam.Api.Domain;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Recurring.GetRecurring;

public sealed class GetRecurringQuery(IDbConnectionFactory factory)
{
    private const string Sql = """
        SELECT  t.[description], t.[date], t.[amount], t.[type], t.[externalId],
                t.[bucket], c.[name] AS [categoryName]
        FROM    [Transactions] t
        JOIN    [Categories] c ON c.[id] = t.[categoryId]
        WHERE   t.[owner] = @Owner
        ORDER BY t.[date] ASC;

        SELECT  [id], [owner], [description], [status], [note], [createdAt], [updatedAt]
        FROM    [RecurringVerdicts]
        WHERE   [owner] = @Owner;
        """;

    public async Task<GetRecurringResponse> ExecuteAsync(GetRecurringRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = RecurringOwner.Parse(request.Owner);

        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, new { Owner = owner.ToString() }, cancellationToken: ct));

        var rows = await grid.ReadAsync<CandidateRow>();
        var verdicts = await grid.ReadAsync<RecurringVerdictRecord>();

        var candidates = rows.Select(r => new RecurringCandidate
        {
            Description = r.Description,
            Date = r.Date,
            Amount = r.Amount,
            Type = r.Type,
            Bank = RuleEngine.BankOf(r.ExternalId),
            Bucket = r.Bucket,
            CategoryName = r.CategoryName,
        }).ToList();

        var verdictByDescription = verdicts.ToDictionary(v => v.Description, StringComparer.Ordinal);

        var income = Decorate(
            RecurringDetector.Detect([.. candidates.Where(c => c.Type == TransactionType.Income)]),
            verdictByDescription);

        var expense = Decorate(
            RecurringDetector.Detect([.. candidates.Where(c => c.Type == TransactionType.Expense)]),
            verdictByDescription);

        return new GetRecurringResponse
        {
            Owner = owner,
            Income = income,
            Expense = expense,
            CommittedOutPerMonth = Committed(expense),
            CommittedInPerMonth = Committed(income),
        };
    }

    /// Only confirmed, active, non-Ignore series count toward the committed
    /// total. Ignore is where card payments and transfers live — including them
    /// would report the entire card balance as a monthly commitment.
    private static double Committed(IReadOnlyList<RecurringSeriesView> series) =>
        series
            .Where(s => s.Status == RecurringStatus.Confirmed && s.Active && s.Bucket != Domain.Bucket.Ignore)
            .Sum(s => s.MonthlyEquivalent);

    private static List<RecurringSeriesView> Decorate(
        List<RecurringSeries> series,
        Dictionary<string, RecurringVerdictRecord> verdictByDescription) =>
        [.. series.Select(s =>
        {
            var verdict = verdictByDescription.GetValueOrDefault(s.Description);
            return new RecurringSeriesView
            {
                Description = s.Description,
                Kind = s.Kind,
                Cadence = s.Cadence,
                MedianGapDays = s.MedianGapDays,
                Occurrences = s.Occurrences,
                Irregularity = s.Irregularity,
                Coverage = s.Coverage,
                LastDate = s.LastDate,
                LastAmount = s.LastAmount,
                AverageAmount = s.AverageAmount,
                NextDueDate = s.NextDueDate,
                Bucket = s.Bucket,
                CategoryName = s.CategoryName,
                Bank = s.Bank,
                Active = s.Active,
                DaysSinceLast = s.DaysSinceLast,
                BankDataEndsAt = s.BankDataEndsAt,
                Status = verdict?.Status,
                Note = verdict?.Note,
                MonthlyEquivalent = RecurringDetector.MonthlyEquivalent(s),
            };
        })];

    private sealed class CandidateRow
    {
        public string Description { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public TransactionType Type { get; set; }
        public string? ExternalId { get; set; }
        public Bucket? Bucket { get; set; }
        public string CategoryName { get; set; } = "";
    }
}
