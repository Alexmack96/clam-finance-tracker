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
    /// Strings are accepted as well as numbers because that is what this
    /// converter *writes* — a client posting back a body it was just given sends
    /// `"12.34"`, not `12.34`.
    ///
    /// Failure is signalled with JsonException, not by letting decimal.Parse
    /// throw FormatException. Only the former is understood as a binding error:
    /// a FormatException escapes the binder as an unhandled exception, so
    /// `{"amount":"twenty"}` answered 500 instead of naming the field in a 400.
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();

            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new JsonException($"'{raw}' is not a number.");
        }

        return reader.TryGetDecimal(out var value)
            ? value
            : throw new JsonException("Value is not within the range of a decimal.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        // Prisma emits "12.34", not "12.3400" — trim the scale the DECIMAL(18,2)
        // column pads on, so the strings compare equal against the Express API.
        writer.WriteStringValue(value.ToString("0.##", CultureInfo.InvariantCulture));
}
