using System.Text;

namespace Clam.Sync;

/// RFC 4180, plus one rule the format does not have: an **unquoted empty field is
/// NULL, a quoted empty field is the empty string**.
///
/// CSV cannot otherwise tell those apart, and this data needs it to. A NULL
/// [note] and an empty-string [note] are different rows, and a NULL
/// [statementFileId] coerced to "" is a foreign key pointing at a statement that
/// does not exist. So the writer quotes every non-null value, whatever it is, and
/// writes nothing at all for null. Postgres' COPY … WITH CSV settled on the same
/// convention for the same reason.
///
/// Small files by design — the whole SQLite database is under two megabytes — so
/// the reader takes the text in one piece rather than streaming.
public static class Csv
{
    public static void WriteRow(TextWriter writer, IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) writer.Write(',');
            if (fields[i] is not { } value) continue;

            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }

        writer.Write('\n');
    }

    public static List<string?[]> Parse(string text)
    {
        var rows = new List<string?[]>();
        var row = new List<string?>();
        var field = new StringBuilder();
        var quoted = false;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (inQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }

                // Doubled quote inside a quoted field is one literal quote.
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                inQuotes = false;
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0 && !quoted:
                    quoted = true;
                    inQuotes = true;
                    break;

                case ',':
                    row.Add(Take(field, ref quoted));
                    break;

                case '\n':
                    row.Add(Take(field, ref quoted));
                    rows.Add([.. row]);
                    row.Clear();
                    break;

                // Tolerated on read so a file that has been through a Windows
                // editor still parses; never written.
                case '\r':
                    break;

                default:
                    field.Append(character);
                    break;
            }
        }

        // A file ending in a newline leaves nothing behind; one that does not
        // still owes its last row.
        if (row.Count > 0 || field.Length > 0 || quoted)
        {
            row.Add(Take(field, ref quoted));
            rows.Add([.. row]);
        }

        return rows;
    }

    private static string? Take(StringBuilder field, ref bool quoted)
    {
        var value = quoted || field.Length > 0 ? field.ToString() : null;
        field.Clear();
        quoted = false;
        return value;
    }
}
