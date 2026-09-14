using System.Data;
using System.Globalization;
using System.Text;
using Clam.Migrate;
using Clam.Sync;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

// Tops the SQL Server database up from a fresh snapshot of the Express service's
// SQLite one, in two steps that can be run separately: dump every migrated table
// to CSV, then insert whatever the target does not already have.
//
// This is not Clam.Migrate. That is the one-shot cutover, and it refuses a target
// that already holds rows because a second pass would double them. This is the
// repeatable one, for while both services are still running and production keeps
// filling the SQLite file up:
//
//     scripts/sync-prod-to-sql.ps1 -DryRun
//     scripts/sync-prod-to-sql.ps1
//
// or, once prod.db is already on disk:
//
//     dotnet run --project api-dotnet/tools/Clam.Sync -- \
//         --sqlite prod.db --csv .sync/prod-csv \
//         --target "Server=tcp:...;Database=ClamFinanceDev;..." --dry-run
//
// **It only ever inserts.** A row already present is left exactly as it is, so a
// note written or a category corrected on the target survives every re-run. The
// cost of that is the other direction: an edit made in production after a row was
// copied does not follow it here, and a row deleted in production is not deleted
// here either.
//
// The CSVs are not a formality. They are the artefact to look at when a number
// disagrees — diffable between two pulls, and readable without either database.

var options = SyncOptions.Parse(args);
if (options is null) return 1;

// Fails now, on a mismatch between this tool's key metadata and MigrationPlan's
// column lists, rather than at the point a column quietly stops being copied.
SyncPlan.Validate();

if (!options.LoadOnly)
{
    if (!File.Exists(options.SqlitePath))
    {
        Console.Error.WriteLine($"No SQLite database at {options.SqlitePath}");
        return 1;
    }

    Dump(options.SqlitePath, options.CsvDirectory);
}

return options.DumpOnly ? 0 : await LoadAsync(options);

static void Dump(string sqlitePath, string csvDirectory)
{
    Directory.CreateDirectory(csvDirectory);

    using var source = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());

    source.Open();

    Console.WriteLine($"Dump   : {Path.GetFullPath(sqlitePath)}");
    Console.WriteLine($"      -> {Path.GetFullPath(csvDirectory)}\n");

    var total = 0;

    foreach (var move in MigrationPlan.Tables)
    {
        var path = Path.Combine(csvDirectory, $"{move.Target}.csv");

        // No BOM and \n endings, so the files diff cleanly between two pulls.
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Csv.WriteRow(writer, move.Columns);

        using var select = source.CreateCommand();
        select.CommandText =
            $"SELECT {string.Join(", ", move.Columns.Select(c => $"\"{c}\""))} FROM \"{move.Source}\";";

        using var rows = select.ExecuteReader();
        var fields = new string?[move.Columns.Length];
        var count = 0;

        while (rows.Read())
        {
            for (var i = 0; i < fields.Length; i++)
                fields[i] = rows.IsDBNull(i) ? null : Text(rows.GetValue(i), move.Target, move.Columns[i]);

            Csv.WriteRow(writer, fields);
            count++;
        }

        total += count;
        Console.WriteLine($"  {move.Source,-24} -> {move.Target,-24} {count,6}");
    }

    Console.WriteLine($"\n{total} rows written.\n");
}

/// One SQLite value as the text the CSV carries. Types are not preserved and do
/// not need to be: the load reads them back against the target's own columns.
static string Text(object value, string table, string column) => value switch
{
    string text => text,

    // Nothing in this schema is a blob, so one appearing means the schema moved
    // and this format has no encoding for it. Better to stop than to write
    // something that round-trips into the wrong bytes.
    byte[] => throw new NotSupportedException(
        $"[{table}].[{column}] holds binary, which these CSVs cannot carry."),

    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
};

static async Task<int> LoadAsync(SyncOptions options)
{
    await using var target = new SqlConnection(options.TargetConnectionString);
    await target.OpenAsync();

    Console.WriteLine($"Load   : {Path.GetFullPath(options.CsvDirectory)}");
    Console.WriteLine($"      -> {target.Database} on {target.DataSource}");
    Console.WriteLine(options.DryRun
        ? "Mode   : dry run, everything is rolled back at the end\n"
        : "Mode   : writing\n");

    // One transaction for the whole load. Partway through is the worst place to
    // stop: the tables go in foreign key order, so a failure at Transactions
    // would leave Categories and StatementFiles already committed and the next
    // run would have to work out which.
    await using var transaction = (SqlTransaction)await target.BeginTransactionAsync();

    // Filled in as each remappable table is loaded, and read by the tables after
    // it — which is why MigrationPlan's foreign key order is also the load order.
    var remaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    var inserted = 0;
    var skipped = 0;

    // Rules are first-match-wins on position and nothing enforces that positions
    // are unique, so bringing production's in alongside rules this database made
    // for itself does not fail — it silently interleaves them, and which one wins
    // a transaction becomes arbitrary. Counted before, warned about after.
    var rulesBefore = await ScalarAsync(target, transaction, "SELECT COUNT(*) FROM [Rules];");
    var rulesAdded = 0;

    Console.WriteLine($"  {"table",-24} {"staged",6} {"new",8} {"present",8}");

    foreach (var (move, keys) in SyncPlan.Ordered())
    {
        var path = Path.Combine(options.CsvDirectory, $"{move.Target}.csv");
        if (!File.Exists(path))
        {
            await transaction.RollbackAsync();
            Console.Error.WriteLine($"\nNo {path}. Run the dump first, or drop --load-only.");
            return 1;
        }

        var (staged, added, remapped) = await SyncTableAsync(target, transaction, move, keys, path, remaps);

        inserted += added;
        skipped += staged - added;
        if (move.Target == "Rules") rulesAdded = added;

        var note = remapped > 0 ? $"   ({remapped} ids remapped onto rows already there)" : string.Empty;
        Console.WriteLine($"  {move.Target,-24} {staged,6} {added,8} {staged - added,8}{note}");
    }

    Console.WriteLine($"\n{inserted} rows inserted, {skipped} already present.");

    if (rulesAdded > 0 && rulesBefore > 0)
    {
        Console.WriteLine(
            $"\nWARNING: {rulesAdded} rules went in alongside {rulesBefore} this database already had.\n" +
            "         Rules are first-match-wins on [position] and positions are not unique, so the two\n" +
            "         sets now interleave. Check the rule order on the Rules page before trusting a\n" +
            "         re-categorisation.");
    }

    if (options.DryRun)
    {
        await transaction.RollbackAsync();
        Console.WriteLine("Dry run: rolled back. Nothing was written.");
    }
    else
    {
        await transaction.CommitAsync();
    }

    return 0;
}

/// Bulk-copies one CSV into a temp table shaped like the target, then inserts the
/// rows the target does not already have.
///
/// The comparison happens in SQL rather than by reading every key back into a
/// HashSet, so the cost is one round trip per table however large it grows.
static async Task<(int Staged, int Inserted, int Remapped)> SyncTableAsync(
    SqlConnection target,
    SqlTransaction transaction,
    TableMove move,
    SyncKey keys,
    string csvPath,
    Dictionary<string, Dictionary<string, string>> remaps)
{
    var columns = string.Join(", ", move.Columns.Select(c => $"[{c}]"));

    // The shape is read back off the target, so every value is converted into
    // what the destination column wants. See SqliteValue for why that direction.
    using var shape = new DataTable();
    await using (var schema = target.CreateCommand())
    {
        schema.Transaction = transaction;
        schema.CommandText = $"SELECT TOP 0 {columns} FROM [{move.Target}];";

        await using var reader = await schema.ExecuteReaderAsync();
        shape.Load(reader);
    }

    var rows = Csv.Parse(await File.ReadAllTextAsync(csvPath));
    if (rows.Count == 0) throw new InvalidDataException($"{csvPath} is empty; it should at least have a header.");

    var header = rows[0].Select(field => field ?? string.Empty).ToArray();
    if (!header.SequenceEqual(move.Columns, StringComparer.Ordinal))
    {
        throw new InvalidDataException(
            $"{csvPath} header is [{string.Join(", ", header)}], expected " +
            $"[{string.Join(", ", move.Columns)}]. It was dumped by an older MigrationPlan — re-run the dump.");
    }

    // Foreign keys into a table whose ids were remapped have to be rewritten
    // before the row is staged, or they point at ids this database never had.
    var rewrites = (keys.ForeignKeys ?? [])
        .Where(foreignKey => remaps.ContainsKey(foreignKey.References))
        .Select(foreignKey => (Index: Array.IndexOf(move.Columns, foreignKey.Column), Map: remaps[foreignKey.References]))
        .ToArray();

    foreach (var source in rows.Skip(1))
    {
        if (source.Length != move.Columns.Length)
            throw new InvalidDataException($"{csvPath} has a row of {source.Length} fields; expected {move.Columns.Length}.");

        foreach (var (index, map) in rewrites)
        {
            if (source[index] is { } reference && map.TryGetValue(reference, out var replacement))
                source[index] = replacement;
        }

        var row = shape.NewRow();
        for (var i = 0; i < move.Columns.Length; i++)
            row[i] = SqliteValue.Coerce(source[i], shape.Columns[i].DataType);

        shape.Rows.Add(row);
    }

    if (shape.Rows.Count == 0) return (0, 0, 0);

    // SELECT ... INTO copies the column types and nothing else — no identity, no
    // constraints, no indexes — which is exactly what a staging heap wants.
    var stage = $"#stage_{move.Target}";
    await ExecuteAsync(target, transaction, $"SELECT TOP 0 {columns} INTO [{stage}] FROM [{move.Target}];");

    using (var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.Default, transaction)
           {
               DestinationTableName = $"[{stage}]",
               BulkCopyTimeout = 0,
           })
    {
        foreach (var column in move.Columns) bulk.ColumnMappings.Add(column, column);
        await bulk.WriteToServerAsync(shape);
    }

    // The key says "this is the same row". The natural key says "the target
    // already has this row under a different id" — a category the system seeder
    // created, a statement re-uploaded here. Either is a reason to leave it be.
    var absent = new List<string> { $"NOT EXISTS (SELECT 1 FROM [{move.Target}] t WHERE {Match(keys.Key)})" };
    if (keys.NaturalKey is { } naturalKey)
        absent.Add($"NOT EXISTS (SELECT 1 FROM [{move.Target}] t WHERE {Match(naturalKey)})");

    var inserted = await ExecuteAsync(target, transaction,
        $"INSERT INTO [{move.Target}] ({columns}) SELECT {columns} FROM [{stage}] s WHERE {string.Join(" AND ", absent)};");

    var remapped = 0;
    if (keys.RemapsIds && keys.NaturalKey is { } matchOn)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = target.CreateCommand())
        {
            command.Transaction = transaction;

            // Rows just inserted have the same id on both sides and drop out of
            // the WHERE; what is left is production's id beside the target's for
            // the same thing.
            command.CommandText =
                $"SELECT s.[id], t.[id] FROM [{stage}] s JOIN [{move.Target}] t ON {Match(matchOn)} WHERE t.[id] <> s.[id];";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) map[reader.GetString(0)] = reader.GetString(1);
        }

        remapped = map.Count;
        if (map.Count > 0) remaps[move.Target] = map;
    }

    await ExecuteAsync(target, transaction, $"DROP TABLE [{stage}];");

    return (shape.Rows.Count, inserted, remapped);

    static string Match(string[] columns) =>
        string.Join(" AND ", columns.Select(column => $"t.[{column}] = s.[{column}]"));
}

static async Task<int> ScalarAsync(SqlConnection target, SqlTransaction transaction, string sql)
{
    await using var command = target.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;

    return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

static async Task<int> ExecuteAsync(SqlConnection target, SqlTransaction transaction, string sql)
{
    await using var command = target.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.CommandTimeout = 0;

    return await command.ExecuteNonQueryAsync();
}

internal sealed record SyncOptions(
    string SqlitePath,
    string CsvDirectory,
    string TargetConnectionString,
    bool DryRun,
    bool DumpOnly,
    bool LoadOnly)
{
    public static SyncOptions? Parse(string[] args)
    {
        var sqlite = "prod.db";
        var csv = Path.Combine(".sync", "prod-csv");
        string? connection = null;
        var dryRun = false;
        var dumpOnly = false;
        var loadOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sqlite" when i + 1 < args.Length: sqlite = args[++i]; break;
                case "--csv" when i + 1 < args.Length: csv = args[++i]; break;
                case "--target" when i + 1 < args.Length: connection = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "--dump-only": dumpOnly = true; break;
                case "--load-only": loadOnly = true; break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return null;
            }
        }

        if (dumpOnly && loadOnly)
        {
            Console.Error.WriteLine("--dump-only and --load-only are the two halves; passing both leaves nothing to do.");
            return null;
        }

        if (connection is null && !dumpOnly)
        {
            Console.Error.WriteLine(
                "Usage: [--sqlite <prod.db>] [--csv <dir>] --target \"<sql server connection string>\"\n" +
                "       [--dry-run] [--dump-only] [--load-only]\n\n" +
                "--target is only optional with --dump-only.");
            return null;
        }

        return new SyncOptions(sqlite, csv, connection ?? string.Empty, dryRun, dumpOnly, loadOnly);
    }
}
