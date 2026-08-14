using WebHealth.Domain.Normalization;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class NameNormalizerTests
{
    [Theory]
    [InlineData("  Platform   Support ", "Platform Support", "PLATFORM SUPPORT")]
    [InlineData("Café", "Café", "CAFÉ")]
    [InlineData("Cafe\u0301", "Café", "CAFÉ")]
    public void Name_IsTrimmedCollapsedAndNormalized(
        string input,
        string expectedDisplay,
        string expectedNormalized)
    {
        Assert.Equal(expectedDisplay, NameNormalizer.TrimDisplayName(input));
        Assert.Equal(expectedNormalized, NameNormalizer.Normalize(input));
    }
}
