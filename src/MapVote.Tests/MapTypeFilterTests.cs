using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Tests;

public class MapTypeFilterTests
{
    [Theory]
    [InlineData("de_dust2", "de_", true)]
    [InlineData("cs_office", "de_", false)]
    [InlineData("de_dust2", "DE_", true)]
    [InlineData("dedust2", "de_", false)]
    [InlineData("test_foo_bla", "test_*_bla", true)]
    [InlineData("test__bla", "test_*_bla", true)]
    [InlineData("test_bla", "test_*_bla", false)]
    [InlineData("x_test_foo_bla", "test_*_bla", false)]
    [InlineData("anything", "*", true)]
    [InlineData("de_dust2", "*dust*", true)]
    [InlineData("de_dust2", "*nuke*", false)]
    public void Matches(string map, string pattern, bool expected)
    {
        Assert.Equal(expected, MapTypeFilter.Matches(map, pattern));
    }

    [Fact]
    public void Other_ExcludesEarlierTypedMaps()
    {
        List<FilterTypeConfig> types =
        [
            new() { Id = "bomb", Prefixes = ["de_"] },
            new() { Id = "custom", Prefixes = ["test_*_bla"] },
            new() { Id = "other", Prefixes = [] },
        ];

        Assert.True(MapTypeFilter.MatchesType("de_dust2", types[0], types));
        Assert.True(MapTypeFilter.MatchesType("test_foo_bla", types[1], types));
        Assert.False(MapTypeFilter.MatchesType("de_dust2", types[2], types));
        Assert.False(MapTypeFilter.MatchesType("test_foo_bla", types[2], types));
        Assert.True(MapTypeFilter.MatchesType("cs_office", types[2], types));
    }

    [Fact]
    public void ToRegex_EscapesLiterals()
    {
        Assert.Equal("^test_.*_bla$", MapTypeFilter.ToRegex("test_*_bla"));
        Assert.Equal("^de_$", MapTypeFilter.ToRegex("de_"));
    }
}
