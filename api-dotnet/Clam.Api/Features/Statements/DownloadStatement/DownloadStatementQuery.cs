using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Statements;
using Dapper;

namespace Clam.Api.Features.Statements.DownloadStatement;

public sealed record StatementDownload(byte[] Content, string FileName);

public sealed class DownloadStatementQuery(IDbConnectionFactory factory, IStatementStore store)
{
    private const string Sql = "SELECT [storageKey], [originalName] FROM [StatementFiles] WHERE [id] = @Id;";

    public async Task<Result<StatementDownload>> ExecuteAsync(
        DownloadStatementRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var file = await connection.QuerySingleOrDefaultAsync<StoredFile>(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        if (file is null) return Result<StatementDownload>.NotFound("Statement not found");

        try
        {
            var content = await store.ReadAsync(file.StorageKey, ct);
            return Result<StatementDownload>.Success(new StatementDownload(content, file.OriginalName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The row exists but the bytes do not — 410, not 404. The difference
            // matters: one means "never had it", the other means "the volume
            // lost it", and only the second is worth investigating.
            return Result<StatementDownload>.Unavailable(
                "The stored PDF for this statement is missing from the volume");
        }
    }

    private sealed class StoredFile
    {
        public string StorageKey { get; set; } = "";
        public string OriginalName { get; set; } = "";
    }
}
