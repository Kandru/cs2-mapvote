using System.Text.Json;
using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Tests;

public class ConfigNormalizeTests
{
    [Fact]
    public void Normalize_FillsMissingSectionsAndLists()
    {
        PluginConfig config = JsonSerializer.Deserialize<PluginConfig>(
            """
            {
              "endmatch": null,
              "rtv": null,
              "nominations": null,
              "display": null,
              "filters": null,
              "catalog": null,
              "maps_votable": null
            }
            """)!;

        config.Normalize();

        Assert.NotNull(config.Endmatch);
        Assert.NotNull(config.Rtv);
        Assert.NotNull(config.Nominations);
        Assert.NotNull(config.Display);
        Assert.NotNull(config.Filters);
        Assert.NotNull(config.Catalog);
        Assert.NotNull(config.MapsVotable);
        Assert.NotEmpty(config.Filters.Types);
        Assert.Equal(
            ["alphabetical", "most_positive", "most_negative"],
            config.Filters.Sorts.Select(static sort => sort.Id));
        Assert.Equal(ConfigDefaults.DefaultDisplayOrder, config.Display.Order);
        Assert.Equal(ConfigDefaults.DefaultSuccessCommand, config.Rtv.SuccessCommand);
        Assert.Equal(MapCatalog.MaxMaps, config.Endmatch.MaxMaps);
    }

    [Fact]
    public void Normalize_PreservesExplicitEmptySortList()
    {
        PluginConfig config = new()
        {
            Filters = new FiltersConfig { Sorts = [] },
        };

        config.Normalize();

        Assert.Empty(config.Filters.Sorts);
    }

    [Fact]
    public void Normalize_ClampsNonPositiveMaxMapsAndBlankOrder()
    {
        PluginConfig config = new()
        {
            Display = new DisplayConfig { Order = "  " },
            Endmatch = new EndmatchConfig { MaxMaps = 0 },
        };

        config.Normalize();

        Assert.Equal(MapCatalog.MaxMaps, config.Endmatch.MaxMaps);
        Assert.Equal(ConfigDefaults.DefaultDisplayOrder, config.Display.Order);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        PluginConfig config = new();
        config.Normalize();
        int types = config.Filters.Types.Count;
        int sorts = config.Filters.Sorts.Count;

        config.Normalize();

        Assert.Equal(types, config.Filters.Types.Count);
        Assert.Equal(sorts, config.Filters.Sorts.Count);
    }
}
