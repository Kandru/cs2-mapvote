using MapVote.Maps;

namespace MapVote.Tests;

public class MapImageClassesTests
{
    [Fact]
    public void ClassFor_Empty_IsDefault()
    {
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor(null, MapOrigin.Local));
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor("", MapOrigin.Workshop));
    }

    [Fact]
    public void ClassFor_Local_UsesMapPrefix()
    {
        Assert.Equal("mv-map-map_de_dust2", MapImageClasses.ClassFor("de_dust2", MapOrigin.Local));
    }

    [Fact]
    public void ClassFor_Workshop_UsesWsPrefix()
    {
        Assert.Equal("mv-map-ws_de_dust2", MapImageClasses.ClassFor("DE_Dust2", MapOrigin.Workshop));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Bad Name")]
    [InlineData("de_foo_vanity")]
    [InlineData("de_foo_skybox")]
    [InlineData("workshop_preview_1")]
    [InlineData("lobby_mapveto")]
    [InlineData("maps/de_dust2")]
    public void ClassFor_InvalidName_IsDefault(string mapName)
    {
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor(mapName, MapOrigin.Local));
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor(mapName, MapOrigin.Workshop));
    }

    [Fact]
    public void ClassFor_ExtensionAndCase_IsNormalized()
    {
        Assert.Equal("mv-map-map_de_dust2", MapImageClasses.ClassFor("DE_Dust2.VPK", MapOrigin.Local));
    }

    [Fact]
    public void Prefixes_MatchVisibilityKeys()
    {
        Assert.Equal("mv-map-map_", MapImageClasses.StockPrefix);
        Assert.Equal("mv-map-ws_", MapImageClasses.WorkshopPrefix);
        Assert.NotEqual(MapImageClasses.StockPrefix, MapImageClasses.WorkshopPrefix);
    }
}
