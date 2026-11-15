using MapVote.Maps;

namespace MapVote.Tests;

public class MapNameDisplayTests
{
    [Theory]
    [InlineData("de_dust2", "Dust2")]
    [InlineData("cs_office", "Office")]
    [InlineData("ar_baggage", "Baggage")]
    [InlineData("fy_iceworld", "Iceworld")]
    [InlineData("dust2", "Dust2")]
    [InlineData("rush_001", "001")]
    [InlineData("de_dust2_night", "Dust2 Night")]
    [InlineData("de_dust2_v2", "Dust2 V2")]
    [InlineData("de_cache_2v2", "Cache 2v2")]
    [InlineData("some_custom_map", "Custom Map")]
    [InlineData("de__dust2", "Dust2")]
    [InlineData("de_dust2__night", "Dust2 Night")]
    [InlineData("de_", "De")]
    [InlineData("de___", "")]
    [InlineData("", "")]
    public void Format_DropsPrefixAndCapitalizesWords(string map, string expected)
    {
        Assert.Equal(expected, MapNameDisplay.Format(map));
    }

    [Fact]
    public void ForHud_WrapsInNonBreakingSpaces()
    {
        char nbsp = MapNameDisplay.NonBreakingSpace;
        Assert.Equal(160, (int)nbsp);
        Assert.Equal($"{nbsp}Overpass{nbsp}", MapNameDisplay.ForHud("Overpass"));
    }

    [Fact]
    public void ForHud_LeavesEmptyTextEmpty()
    {
        Assert.Equal(string.Empty, MapNameDisplay.ForHud(string.Empty));
    }
}
