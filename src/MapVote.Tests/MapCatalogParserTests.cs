using MapVote.Maps;

namespace MapVote.Tests;

public class MapCatalogParserTests
{
    [Fact]
    public void ParseLocalMaps_NormalizesAcceptsAndCountsSkipped()
    {
        const string response = """
            de_dust2
            DE_MIRAGE.BSP
            workshop_preview_1

            de_dust2
            """;

        (List<MapEntry> maps, int skipped) = MapCatalogParser.ParseLocalMaps(response);

        Assert.Equal(2, maps.Count);
        Assert.Equal("de_dust2", maps[0].Name);
        Assert.Equal("de_mirage", maps[1].Name);
        Assert.All(maps, static map => Assert.Equal(MapOrigin.Local, map.Origin));
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void ParseLocalMaps_ReturnsEmptyForBlankReply()
    {
        Assert.Empty(MapCatalogParser.ParseLocalMaps(null).Maps);
        Assert.Empty(MapCatalogParser.ParseLocalMaps(string.Empty).Maps);
    }
}
