using System.Globalization;
using System.Text;

namespace WebHealth.Application.Reporting;

/// <summary>
/// One cell, carrying whether its content came from somewhere a person could control.
/// </summary>
/// <remarks>
/// The distinction exists so the formula-injection guard can be applied to text without
/// mangling numbers. A blanket guard would rewrite the perfectly ordinary value <c>-1</c> as
/// <c>'-1</c>, which corrupts every negative number in the file to defend against a risk that
/// generated numerals do not carry. Text a user typed — a client name, a URL — is guarded;
/// values this system formatted itself are not.
/// </remarks>
public readonly record struct CsvField(string Value, bool IsUserText)
{
    /// <summary>User-supplied or user-influenced text. Guarded against formula injection.</summary>
    public static CsvField Text(string? value) => new(value ?? string.Empty, true);

    /// <summary>
    /// Machine-formatted text from a closed vocabulary this system defines — a status, a
    /// monitor type, a rule key. Not user input, so it needs no guard.
    /// </summary>
    public static CsvField Token(string? value) => new(value ?? string.Empty, false);

    public static CsvField Number(double? value) => new(
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Count(long? value) => new(
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Flag(bool value) => new(value ? "true" : "false", false);

    /// <summary>ISO-8601 with the offset spelled out, so a reader never has to guess the zone.</summary>
    public static CsvField Timestamp(DateTimeOffset? value) => new(
        value?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture) ?? string.Empty,
        false);

    public static CsvField Date(DateOnly? value) => new(
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        false);
}

/// <summary>
/// RFC 4180 CSV with the two concessions real recipients need: a UTF-8 byte-order mark, because
/// Excel otherwise opens a UTF-8 file as the local ANSI code page and mangles every non-ASCII
/// name in it; and a formula-injection guard on user text, because a spreadsheet evaluates a
/// cell beginning with certain characters as a formula rather than showing it.
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Characters that make a spreadsheet treat a cell as a formula rather than as text. The
    /// tab and carriage return are included because Excel strips leading whitespace before
    /// deciding, so <c>\t=cmd</c> reaches the formula parser just as <c>=cmd</c> does.
    /// </summary>
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

        // new UTF8Encoding(true) rather than Encoding.UTF8 so the preamble is written
        // explicitly; GetBytes never emits it on its own.
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

    /// <summary>
    /// Prefixes a leading formula trigger with an apostrophe, which spreadsheets read as "the
    /// rest of this cell is text". The apostrophe is part of the exported value: this changes
    /// what the file says in order to stop the file from being executable, which is the trade
    /// the guard exists to make.
    /// </summary>
    private static string Guard(string value) =>
        value.Length > 0 && FormulaTriggers.Contains(value[0]) ? $"'{value}" : value;
}
