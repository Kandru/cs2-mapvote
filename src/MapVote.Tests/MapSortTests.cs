using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Tests;

public class MapSortTests
{
    private static readonly IReadOnlyList<string> Catalog =
        ["de_dust2", "de_mirage", "cs_office", "de_inferno"];

    [Fact]
    public void Apply_MostPlayed_OrdersByPlayCountDesc()
    {
        Dictionary<string, int> plays = new(StringComparer.Ordinal)
        {
            ["de_mirage"] = 5,
            ["de_dust2"] = 2,
            ["de_inferno"] = 2,
        };

        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "most_played", By = "plays", Order = "desc" },
            m => plays.GetValueOrDefault(m),
            _ => 0);

        Assert.Equal(["de_mirage", "de_dust2", "de_inferno", "cs_office"], result);
    }

    [Fact]
    public void Apply_LeastPlayed_OrdersByPlayCountAsc()
    {
        Dictionary<string, int> plays = new(StringComparer.Ordinal)
        {
            ["de_mirage"] = 5,
            ["de_dust2"] = 1,
        };

        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "least_played", By = "plays", Order = "asc" },
            m => plays.GetValueOrDefault(m),
            _ => 0);

        Assert.Equal(["cs_office", "de_inferno", "de_dust2", "de_mirage"], result);
    }

    [Fact]
    public void Apply_Name_OrdersAlphabetically()
    {
        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "alpha", By = "name", Order = "asc" },
            _ => 0,
            _ => 0);

        Assert.Equal(["cs_office", "de_dust2", "de_inferno", "de_mirage"], result);
    }

    [Fact]
    public void Apply_Name_UsesCleanedDisplayKeyWhenProvided()
    {
        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "alpha", By = "name", Order = "asc" },
            _ => 0,
            _ => 0,
            nameKey: MapNameDisplay.Format);

        Assert.Equal(["de_dust2", "de_inferno", "de_mirage", "cs_office"], result);
    }

    [Fact]
    public void Apply_MostPositive_OrdersByRatingScoreDesc()
    {
        Assert.Equal(
            ["cs_office", "de_mirage", "de_dust2", "de_inferno"],
            ApplyRatingSort("desc", new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["cs_office"] = 4,
                ["de_mirage"] = 1,
                ["de_inferno"] = -2,
            }));
    }

    [Fact]
    public void Apply_MostNegative_OrdersByRatingScoreAsc()
    {
        Assert.Equal(
            ["de_inferno", "de_dust2", "de_mirage", "cs_office"],
            ApplyRatingSort("asc", new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["cs_office"] = 4,
                ["de_mirage"] = 1,
                ["de_inferno"] = -2,
            }));
    }

    [Fact]
    public void Apply_RatingSort_OrdersEveryMapBeyondTheOldGridLimit()
    {
        List<string> maps = Enumerable.Range(0, 150)
            .Select(static i => $"de_map{i:000}")
            .ToList();

        List<string> result = MapSort.Apply(
            maps,
            maps,
            new SortConfig { Id = "most_positive", By = "rating", Order = "desc" },
            _ => 0,
            m => int.Parse(m.AsSpan("de_map".Length)));

        Assert.Equal(150, result.Count);
        Assert.Equal("de_map149", result[0]);
        Assert.Equal("de_map000", result[^1]);
    }

    [Fact]
    public void Apply_KeepsEveryMap()
    {
        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "most_negative", By = "rating", Order = "asc" },
            _ => 0,
            m => m == "de_dust2" ? 10 : 0);

        Assert.Equal(Catalog.Count, result.Count);
        Assert.Equal(Catalog.OrderBy(static m => m, StringComparer.Ordinal), result.OrderBy(static m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void Apply_FavoriteOrdersFavoritesFirstWithoutDroppingMaps()
    {
        HashSet<string> favorites = new(StringComparer.Ordinal) { "de_inferno", "cs_office" };

        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "favorites", By = "favorite", Order = "desc" },
            _ => 0,
            _ => 0,
            favorites: favorites);

        Assert.Equal(["cs_office", "de_inferno", "de_dust2", "de_mirage"], result);
    }

    [Fact]
    public void Apply_UnknownBy_KeepsCatalogOrder()
    {
        List<string> reordered = ["de_inferno", "cs_office", "de_mirage", "de_dust2"];

        List<string> result = MapSort.Apply(
            reordered,
            reordered,
            new SortConfig { Id = "custom", By = "something", Order = "desc" },
            _ => 0,
            _ => 0);

        Assert.Equal(reordered, result);
    }

    [Fact]
    public void Apply_TieBreaksByCatalogOrder()
    {
        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "most_positive", By = "rating", Order = "desc" },
            _ => 0,
            _ => 0);

        Assert.Equal(Catalog, result);
    }

    private static List<string> ApplyRatingSort(string order, Dictionary<string, int> scores) =>
        MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "rating", By = "rating", Order = order },
            _ => 0,
            m => scores.GetValueOrDefault(m));
}
