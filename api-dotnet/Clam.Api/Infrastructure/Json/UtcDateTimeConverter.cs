using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clam.Api.Infrastructure.Json;

/// SQL Server hands back DATETIME2 as `DateTimeKind.Unspecified`, which
/// System.Text.Json then writes without a trailing `Z`. Prisma always emits
/// `2026-03-10T00:00:00.000Z`, and the client feeds these straight into
/// `new Date(...)` — an unmarked string is parsed as *local* time there, which
/// shifts rows across month boundaries and quietly corrupts the monthly charts.
///
/// Everything in the database is already UTC, so stamping the kind is correct
/// rather than merely convenient.
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string PrismaFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(
            DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString(PrismaFormat, CultureInfo.InvariantCulture));
}
