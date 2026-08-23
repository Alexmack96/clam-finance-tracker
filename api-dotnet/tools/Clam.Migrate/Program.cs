using System.Data;
using System.Globalization;
using Clam.Migrate;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

// Moves the Express service's SQLite database into the .NET API's SQL Server one.
//
// This exists because re-uploading the statements does not bring the data back.
// Statements recover bank transactions; they do not recover categories, rules,
// tabs, notes, investment accounts and their snapshots, recurring verdicts, or
// the Monzo history — Monzo's API only reaches back 90 days, so what is in the
// staging table is all there will ever be.
//
// One shot, by hand, with the target schema freshly applied. It refuses a target
// that already holds data rather than making a second pass and doubling it.
//
//     dotnet run --project api-dotnet/tools/Clam.Migrate -- \
//         --sqlite prod.db \
//         --target "Server=tcp:...;Database=ClamFinance;User ID=...;Password=..." \
//         --workos alexmackintosh96@gmail.com=user_01ABC
//
// Add --dry-run to read and convert everything without writing, which is the
// cheap way to find a schema drift before touching the real database.

var options = Options.Parse(args);
if (options is null) return 1;

if (!File.Exists(options.SqlitePath))
{
    Console.Error.WriteLine($"No SQLite database at {options.SqlitePath}");
    return 1;
}

await using var source = new SqliteConnection(
    new SqliteConnectionStringBuilder
    {
        DataSource = options.SqlitePath,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString());

await source.OpenAsync();

await using var target = new SqlConnection(options.TargetConnectionString);
await target.OpenAsync();

Console.WriteLine($"Source : {Path.GetFullPath(options.SqlitePath)}");
Console.WriteLine($"Target : {target.Database} on {target.DataSource}");
Console.WriteLine(options.DryRun ? "Mode   : dry run, nothing will be written\n" : "Mode   : writing\n");

if (!options.DryRun && !await TargetIsEmptyAsync(target, options.Force))
    return 1;

var total = 0;

foreach (var table in MigrationPlan.Tables)
{
    var moved = await MoveAsync(source, target, table, options.DryRun);
    total += moved;
    Console.WriteLine($"  {table.Source,-24} → {table.Target,-24} {moved,6}");
}

Console.WriteLine($"\n{total} rows{(options.DryRun ? " would move" : " moved")}.");

if (options.WorkOsIds.Count > 0 && !options.DryRun)
{
    foreach (var (email, sub) in options.WorkOsIds)
    {
        await using var command = target.CreateCommand();
        command.CommandText = "UPDATE [Users] SET [workOsUserId] = @sub WHERE [email] = @email;";
        command.Parameters.AddWithValue("@sub", sub);
        command.Parameters.AddWithValue("@email", email);

        var rows = await command.ExecuteNonQueryAsync();
        Console.WriteLine(rows == 1
            ? $"Linked {email} to {sub}."
            : $"WARNING: {email} matched {rows} users — workOsUserId not set as intended.");
    }
}
else if (options.WorkOsIds.Count == 0 && !options.DryRun)
{
    // Not a failure, but it is the one thing that will not announce itself: the
    // API answers GET /api/me with 404 for an unlinked user, and the client
    // reads that as "provision me", which creates a *second* row.
    Console.WriteLine(
        "\nNOTE: no --workos mappings given, so every migrated user has a null workOsUserId.\n" +
        "      Set them before anyone signs in, or first sign-in creates a duplicate user row.");
}

return 0;

/// A target with rows in it means this has already run, or the schema was applied
/// over live data. Either way a second pass duplicates everything, and nothing
/// here is idempotent enough to be worth risking on a guess.
static async Task<bool> TargetIsEmptyAsync(SqlConnection target, bool force)
{
    foreach (var table in MigrationPlan.Tables)
    {
        await using var command = target.CreateCommand();

        // Categories is expected to hold exactly the Uncategorised row that
        // db/schema.sql inserts, and Rules whatever the system category seeder
        // created on the API's first boot. Neither is a reason to stop.
        command.CommandText = table.Target switch
        {
            "Categories" => "SELECT COUNT(*) FROM [Categories] WHERE [name] <> 'Uncategorised';",
            "Rules" or "RuleConditions" => "SELECT 0;",
            _ => $"SELECT COUNT(*) FROM [{table.Target}];",
        };

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (count == 0) continue;

        if (force)
        {
            Console.WriteLine($"WARNING: [{table.Target}] already holds {count} rows — continuing because --force.");
            continue;
        }

        Console.Error.WriteLine(
            $"[{table.Target}] already holds {count} rows. This tool is a one-shot move and would " +
            "duplicate them.\nApply db/schema.sql to get a clean database, or pass --force if you " +
            "are certain.");
        return false;
    }

    // The seeded bucket rules would sit in front of the migrated ones and win,
    // because rules are first-match-wins on position.
    await using var rules = target.CreateCommand();
    rules.CommandText = "SELECT COUNT(*) FROM [Rules];";
    var ruleCount = Convert.ToInt32(await rules.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

    if (ruleCount > 0 && !force)
    {
        Console.Error.WriteLine(
            $"[Rules] already holds {ruleCount} rows, which is the system category seeder having run.\n" +
            "Those would outrank every migrated rule on position. Clear them first:\n" +
            "    DELETE FROM [RuleConditions]; DELETE FROM [Rules];\n" +
            "or pass --force.");
        return false;
    }

    return true;
}

/// Reads one table out of SQLite and bulk-copies it in.
///
/// The conversion is driven by the *target* schema, read back as an empty
/// DataTable, rather than by anything declared here. SQLite has no real types —
/// a Prisma DateTime is an ISO string, a Boolean is 0 or 1, a Decimal is a
/// double — so the destination column is the only thing that knows what a value
/// is supposed to become.
static async Task<int> MoveAsync(SqliteConnection source, SqlConnection target, TableMove table, bool dryRun)
{
    var columns = string.Join(", ", table.Columns.Select(c => $"[{c}]"));

    var shape = new DataTable();
    await using (var schema = target.CreateCommand())
    {
        schema.CommandText = $"SELECT TOP 0 {columns} FROM [{table.Target}];";
        using var reader = await schema.ExecuteReaderAsync();
        shape.Load(reader);
    }

    var existing = table.SkipExistingIds
        ? await ExistingIdsAsync(target, table.Target)
        : [];

    await using var select = source.CreateCommand();
    select.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(c => $"\"{c}\""))} FROM \"{table.Source}\";";

    await using var rows = await select.ExecuteReaderAsync();

    while (await rows.ReadAsync())
    {
        if (table.SkipExistingIds && rows["id"] is string id && existing.Contains(id)) continue;

        var row = shape.NewRow();
        for (var i = 0; i < table.Columns.Length; i++)
            row[i] = Coerce(rows.IsDBNull(i) ? null : rows.GetValue(i), shape.Columns[i].DataType);

        shape.Rows.Add(row);
    }

    if (shape.Rows.Count == 0 || dryRun) return shape.Rows.Count;

    using var bulk = new SqlBulkCopy(target) { DestinationTableName = $"[{table.Target}]" };
    foreach (var column in table.Columns) bulk.ColumnMappings.Add(column, column);

    await bulk.WriteToServerAsync(shape);
    return shape.Rows.Count;
}

static async Task<HashSet<string>> ExistingIdsAsync(SqlConnection target, string table)
{
    await using var command = target.CreateCommand();
    command.CommandText = $"SELECT [id] FROM [{table}];";

    var ids = new HashSet<string>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) ids.Add(reader.GetString(0));

    return ids;
}

/// One SQLite value into the CLR type the destination column wants.
static object Coerce(object? value, Type target)
{
    if (value is null or DBNull) return DBNull.Value;

    // Prisma writes DateTime as an ISO 8601 string with an offset. Normalised to
    // UTC rather than kept local: every date the API reads and writes is UTC, and
    // a value an hour out is a transaction on the wrong day at the month boundary.
    if (target == typeof(DateTime))
    {
        return value switch
        {
            string text => DateTime.Parse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            DateTime already => already,
            _ => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture)).UtcDateTime,
        };
    }

    // SQLite has no boolean; Prisma stores 0 and 1.
    if (target == typeof(bool)) return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;

    return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
}

internal sealed record Options(
    string SqlitePath,
    string TargetConnectionString,
    bool DryRun,
    bool Force,
    Dictionary<string, string> WorkOsIds)
{
    public static Options? Parse(string[] args)
    {
        string? sqlite = null, connection = null;
        var dryRun = false;
        var force = false;
        var workOs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sqlite" when i + 1 < args.Length: sqlite = args[++i]; break;
                case "--target" when i + 1 < args.Length: connection = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "--force": force = true; break;
                case "--workos" when i + 1 < args.Length:
                    var pair = args[++i].Split('=', 2);
                    if (pair.Length == 2) workOs[pair[0]] = pair[1];
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return null;
            }
        }

        if (sqlite is null || connection is null)
        {
            Console.Error.WriteLine(
                "Usage: --sqlite <prod.db> --target \"<sql server connection string>\"\n" +
                "       [--workos <email>=<workos user id>] [--dry-run] [--force]");
            return null;
        }

        return new Options(sqlite, connection, dryRun, force, workOs);
    }
}
