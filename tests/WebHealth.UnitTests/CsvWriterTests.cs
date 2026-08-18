using System.Text;
using FluentAssertions;
using WebHealth.Application.Reporting;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// The CSV contract: what a recipient's spreadsheet does with the file, and what it must not do.
/// </summary>
public sealed class CsvWriterTests
{
    [Fact]
    public void TheFileStartsWithAUtf8ByteOrderMark()
    {
        // Without it Excel opens a UTF-8 file as the local code page and mangles every
        // non-ASCII name in it.
        var bytes = CsvWriter.Write(["Name"], [[CsvField.Text("Ünique")]]);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Decode(bytes).Should().Contain("Ünique");
    }

    [Fact]
    public void RowsAreTerminatedWithCarriageReturnLineFeed()
    {
        var text = Decode(CsvWriter.Write(["A", "B"], [[CsvField.Token("1"), CsvField.Token("2")]]));

        text.Should().Be("A,B\r\n1,2\r\n");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("has\r\nboth", "\"has\r\nboth\"")]
    public void FieldsAreQuotedAccordingToRfc4180(string value, string expected)
    {
        var text = Decode(CsvWriter.Write(["A"], [[CsvField.Text(value)]]));

        text.Should().Be($"A\r\n{expected}\r\n");
    }

    [Fact]
    public void AGuardedFieldThatAlsoNeedsQuotingGetsBoth()
    {
        // The guard runs first and the quoting rule then sees the guarded value, so the
        // apostrophe ends up inside the field's own quotes rather than outside them.
        var text = Decode(CsvWriter.Write(["A"], [[CsvField.Text("=a,b")]]));

        text.Should().Be("A\r\n\"'=a,b\"\r\n");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1")]
    [InlineData("-cmd")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=cmd")]
    [InlineData("\r=cmd")]
    public void UserTextBeginningWithAFormulaTriggerIsNeutralised(string value)
    {
        // A spreadsheet evaluates such a cell instead of showing it. Tab and carriage return
        // are included because Excel strips leading whitespace before deciding.
        var text = Decode(CsvWriter.Write(["A"], [[CsvField.Text(value)]]));

        text[3..].TrimStart('"').Should().StartWith("'");
    }

    [Fact]
    public void MachineFormattedValuesAreNotGuarded()
    {
        // A blanket guard would rewrite the ordinary value -1 as '-1 and corrupt every negative
        // number in the file to defend against a risk generated numerals do not carry.
        var text = Decode(CsvWriter.Write(
            ["Number", "Count", "Token"],
            [[CsvField.Number(-1.5), CsvField.Count(-42), CsvField.Token("-Healthy")]]));

        text.Should().EndWith("-1.5,-42,-Healthy\r\n");
    }

    [Fact]
    public void TimestampsCarryTheirOffset()
    {
        var value = new DateTimeOffset(2026, 8, 18, 12, 30, 5, 250, TimeSpan.FromHours(2));

        var text = Decode(CsvWriter.Write(["When"], [[CsvField.Timestamp(value)]]));

        text.Should().EndWith("2026-08-18T12:30:05.250+02:00\r\n");
    }

    [Fact]
    public void NullValuesBecomeEmptyFieldsRatherThanTheWordNull()
    {
        var text = Decode(CsvWriter.Write(
            ["A", "B", "C"],
            [[CsvField.Text(null), CsvField.Number(null), CsvField.Timestamp(null)]]));

        text.Should().EndWith(",,\r\n");
    }

    [Fact]
    public void ARowWithTheWrongFieldCountIsRejected()
    {
        // Silently padding or truncating would shift every later column against its header.
        var act = () => CsvWriter.Write(["A", "B"], [[CsvField.Token("only-one")]]);

        act.Should().Throw<ArgumentException>();
    }

    private static string Decode(byte[] bytes) =>
        new UTF8Encoding(false).GetString(bytes.AsSpan(Encoding.UTF8.GetPreamble().Length));
}
