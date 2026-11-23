using MapVote.Config;
using MapVote.Huds;
using MapVote.Maps;

namespace MapVote.Tests;

public class MapGridViewTests
{
    [Fact]
    public void Build_UsesBaseOrderAndSharedMetadata()
    {
        List<string> order = ["de_mirage", "de_dust2"];
        Dictionary<string, MapOrigin> origins = new(StringComparer.Ordinal)
        {
            ["de_mirage"] = MapOrigin.Workshop,
        };
        Dictionary<string, MapSettings> settings = new(StringComparer.Ordinal)
        {
            ["ws_de_mirage"] = new MapSettings { Added = 42 },
        };

        MapGridView view = MapGridView.Build(order, origins, settings, MapNameDisplay.Format);

        Assert.Equal(2, view.Items.Count);
        Assert.Equal("de_mirage", view.Items[0].Name);
        Assert.Equal(MapOrigin.Workshop, view.Items[0].Origin);
        Assert.Equal(42, view.Items[0].AddedUnix);
        Assert.Equal("Dust2", view.Items[1].DisplayName.Trim(MapNameDisplay.NonBreakingSpace));
        Assert.Equal(0, view.Items[1].AddedUnix);
        Assert.Equal(view.Items[0], view.Find("de_mirage"));
        Assert.Null(view.Find("de_inferno"));
    }

    [Fact]
    public void Build_KeepsRawNamesForIdentity()
    {
        MapGridView view = MapGridView.Build(
            ["de_dust2"],
            new Dictionary<string, MapOrigin>(StringComparer.Ordinal),
            new Dictionary<string, MapSettings>(StringComparer.Ordinal),
            static name => name);

        Assert.Equal("de_dust2", view.Items[0].Name);
        Assert.Equal("de_dust2", view.Items[0].DisplayName.Trim(MapNameDisplay.NonBreakingSpace));
    }
}
