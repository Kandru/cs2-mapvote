using MapVote.Huds;

namespace MapVote.Tests;

public class VoteViewFormatCountTests
{
    [Theory]
    [InlineData(-1, "0")]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.000")]
    [InlineData(1234, "1.234")]
    [InlineData(99999, "99.999")]
    [InlineData(100000, "100k")]
    [InlineData(123456, "123k")]
    [InlineData(999999, "999k")]
    [InlineData(1_000_000, "1M")]
    [InlineData(1_250_000, "1.2M")]
    public void FormatCount_UsesGroupedExactCountsAndCompactBuckets(int value, string expected) =>
        Assert.Equal(expected, VoteView.FormatCount(value, "."));

    [Fact]
    public void FormatCount_UsesTheViewerSeparator() =>
        Assert.Equal("1,234", VoteView.FormatCount(1234, ","));

    [Fact]
    public void FormatCount_FallsBackToCommaWhenSeparatorIsEmpty() =>
        Assert.Equal("1,234", VoteView.FormatCount(1234, string.Empty));
}
