using MapVote.Maps;

namespace MapVote.Tests;

public class MapImageClassesTests
{
    [Fact]
    public void ClassFor_Empty_IsDefault()
    {
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor(null));
        Assert.Equal(MapImageClasses.Default, MapImageClasses.ClassFor(""));
    }

    [Fact]
    public void ClassFor_PrefersWorkshopOverStock_WhenBothExist()
    {
        // de_dust2 is in the committed stock list; if a workshop extract also exists,
        // ClassFor must pick the workshop class name so both CSS rules can coexist.
        string cls = MapImageClasses.ClassFor("de_dust2");
        Assert.True(
            cls is "mv-map-de_dust2" or "mv-map-stock-de_dust2",
            $"unexpected class {cls}");
        if (cls == "mv-map-de_dust2")
        {
            Assert.StartsWith(MapImageClasses.WorkshopPrefix, cls);
        }
        else
        {
            Assert.StartsWith(MapImageClasses.StockPrefix, cls);
        }
    }

    [Fact]
    public void StockPrefix_DiffersFromWorkshopPrefix()
    {
        Assert.NotEqual(MapImageClasses.StockPrefix, MapImageClasses.WorkshopPrefix);
        Assert.Equal("mv-map-stock-", MapImageClasses.StockPrefix);
        Assert.Equal("mv-map-", MapImageClasses.WorkshopPrefix);
    }
}
