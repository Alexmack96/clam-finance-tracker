using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clam.Api.Infrastructure.Json;

/// Prisma serialises `Decimal` through decimal.js, whose `toJSON` returns a
/// *string*. The React client relies on that — `DashboardPage.tsx` types
/// `amount: string`, and `lib/salary.ts` parses it as one.
///
/// A C# `decimal` would serialise as a JSON number instead. Nothing would throw;
/// the client would just start doing string maths on numbers and silently drift.
/// So we match the existing wire format rather than the more natural C# one.
public sealed class DecimalAsStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        // Prisma emits "12.34", not "12.3400" — trim the scale the DECIMAL(18,2)
        // column pads on, so the strings compare equal against the Express API.
        writer.WriteStringValue(value.ToString("0.##", CultureInfo.InvariantCulture));
}
