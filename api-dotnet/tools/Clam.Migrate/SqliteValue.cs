using System.Globalization;

namespace Clam.Migrate;

/// One value out of SQLite — or out of a CSV dumped from it — into the CLR type
/// the destination column wants.
///
/// The conversion is driven by the *target* column and never by anything the
/// source declares, because SQLite has no real types: a Prisma DateTime is an
/// ISO string, a Boolean is 0 or 1, a Decimal is a double. The destination
/// column is the only thing that knows what a value is supposed to become.
///
/// tools/Clam.Sync compiles this file in by <Compile Include="..." Link="..." />
/// rather than by a project reference. Per CLAUDE.md a csproj is a compile and
/// deploy boundary, and one thirty-line rule does not earn one. It lives here
/// because Clam.Migrate is the tool that goes when Express does, and Clam.Sync
/// goes with it.
public static class SqliteValue
{
    public static object Coerce(object? value, Type target)
    {
        if (value is null or DBNull) return DBNull.Value;

        // Prisma writes DateTime as an ISO 8601 string with an offset. Normalised
        // to UTC rather than kept local: every date the API reads and writes is
        // UTC, and a value an hour out is a transaction on the wrong day at the
        // month boundary.
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

        // SQLite has no boolean; Prisma stores 0 and 1. The string arm is for the
        // CSV round trip, where 0 and 1 arrive as text.
        if (target == typeof(bool))
        {
            return value switch
            {
                bool already => already,
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
                string text => bool.Parse(text),
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
            };
        }

        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
