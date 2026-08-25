using System.Globalization;
using System.Text;

namespace WebHealth.Application.Reporting;

public readonly record struct CsvField(string Value, bool IsUserText)
{
    public static CsvField Text(string? value) => new(value ?? string.Empty, true);

    public static CsvField Token(string? value) => new(value ?? string.Empty, false);

    public static CsvField Number(double? value) => new(
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Count(long? value) => new(
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Flag(bool value) => new(value ? "true" : "false", false);

    public static CsvField Timestamp(DateTimeOffset? value) => new(
        value?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Date(DateOnly? value) => new(
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        false);
}

public static class CsvWriter
{
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    private const string LineTerminator = "\r\n";

    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<CsvField>> rows)
    {
        ArgumentOutOfRangeException.ThrowIfZero(headers.Count);

        var builder = new StringBuilder();
        AppendRow(builder, headers.Select(CsvField.Token).ToArray());
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    "Every CSV row must have exactly as many fields as there are headers.",
                    nameof(rows));
            }

            AppendRow(builder, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(builder.ToString())];
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<CsvField> row)
    {
        for (var index = 0; index < row.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(row[index]));
        }

        builder.Append(LineTerminator);
    }

    private static string Escape(CsvField field)
    {
        var value = field.IsUserText ? Guard(field.Value) : field.Value;
        if (!value.AsSpan().ContainsAny(['"', ',', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string Guard(string value) =>
        value.Length > 0 && FormulaTriggers.Contains(value[0]) ? $"'{value}" : value;
}
