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
    [InlineData("", "")]
    public void Format_DropsPrefixAndCapitalizes(string map, string expected)
    {
        Assert.Equal(expected, MapNameDisplay.Format(map));
    }

    [Fact]
    public void OrderByDisplayName_OrdersByCleanedName()
    {
        List<MapEntry> maps =
        [
            new("de_mirage", MapOrigin.Local),
            new("cs_office", MapOrigin.Local),
            new("de_ancient", MapOrigin.Local),
        ];

        List<MapEntry> sorted = MapNameDisplay.OrderByDisplayName(maps);

        Assert.Equal(["de_ancient", "de_mirage", "cs_office"], sorted.ConvertAll(static m => m.Name));
    }
}
