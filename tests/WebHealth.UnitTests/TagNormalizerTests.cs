using FluentAssertions;
using WebHealth.Domain.Normalization;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class TagNormalizerTests
{
    [Fact]
    public void Normalize_TrimsDeduplicatesAndSortsTags()
    {
        var tags = TagNormalizer.Normalize(["  Europe  ", "ASP.NET", "europe", " "]);

        tags.Select(tag => tag.Name).Should().Equal("ASP.NET", "Europe");
        tags.Select(tag => tag.NormalizedName).Should().Equal("ASP.NET", "EUROPE");
    }

    [Fact]
    public void Split_UsesCommaSeparatedInput()
    {
        TagNormalizer.Split("WordPress, Europe, Support")
            .Should().Equal("WordPress", "Europe", "Support");
    }
}
