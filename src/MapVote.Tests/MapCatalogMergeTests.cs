using MapVote.Maps;

namespace MapVote.Tests;

public class MapCatalogMergeTests
{
    [Fact]
    public void Merge_KeepsLocalFirstAndOriginLocalWhenInBoth()
    {
        List<MapEntry> local =
        [
            new("de_dust2", MapOrigin.Local),
            new("de_mirage", MapOrigin.Local),
        ];
        List<MapEntry> workshop =
        [
            new("de_dust2", MapOrigin.Workshop),
            new("fy_iceworld", MapOrigin.Workshop),
        ];

        List<MapEntry> merged = MapCatalog.Merge(local, workshop);

        Assert.Equal(3, merged.Count);
        Assert.Equal("de_dust2", merged[0].Name);
        Assert.Equal(MapOrigin.Local, merged[0].Origin);
        Assert.Equal("de_mirage", merged[1].Name);
        Assert.Equal(MapOrigin.Local, merged[1].Origin);
        Assert.Equal("fy_iceworld", merged[2].Name);
        Assert.Equal(MapOrigin.Workshop, merged[2].Origin);
    }
}
