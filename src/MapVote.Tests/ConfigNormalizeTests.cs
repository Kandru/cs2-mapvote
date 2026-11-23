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
              "maps": null
            }
            """)!;

        config.Normalize();

        Assert.NotNull(config.Endmatch);
        Assert.NotNull(config.Rtv);
        Assert.NotNull(config.Nominations);
        Assert.NotNull(config.Display);
        Assert.NotNull(config.Filters);
        Assert.NotNull(config.Catalog);
        Assert.NotNull(config.Maps);
        Assert.NotEmpty(config.Filters.Types);
        Assert.Equal(
            ["newest", "oldest", "alphabetical", "most_positive", "most_negative"],
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
    public void Normalize_DropsNullAndBlankEntriesAndRepairsLists()
    {
        PluginConfig config = new()
        {
            Filters = new FiltersConfig
            {
                Types = [null!, new() { Id = " bomb ", Prefixes = null! }, new() { Id = "   " }],
                Sorts = [null!, new() { Id = " alpha ", By = null!, Order = " " }],
            },
        };

        config.Normalize();

        FilterTypeConfig type = Assert.Single(config.Filters.Types);
        Assert.Equal("bomb", type.Id);
        Assert.Empty(type.Prefixes);
        SortConfig sort = Assert.Single(config.Filters.Sorts);
        Assert.Equal("alpha", sort.Id);
        Assert.Equal("name", sort.By);
        Assert.Equal("asc", sort.Order);
    }

    [Fact]
    public void Normalize_ClampsUpperMaxMapsAndRtvValues()
    {
        PluginConfig config = new()
        {
            Endmatch = new EndmatchConfig
            {
                MaxMaps = MapCatalog.MaxMaps + 20,
                WinnerRevealSeconds = -4,
            },
            Rtv = new RtvConfig
            {
                Ratio = 2.5f,
                DurationSeconds = -1,
                AnnounceSeconds = -1,
            },
        };

        config.Normalize();

        Assert.Equal(MapCatalog.MaxMaps, config.Endmatch.MaxMaps);
        Assert.Equal(1, config.Endmatch.WinnerRevealSeconds);
        Assert.Equal(1f, config.Rtv.Ratio);
        Assert.Equal(0, config.Rtv.DurationSeconds);
        Assert.Equal(0, config.Rtv.AnnounceSeconds);
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
