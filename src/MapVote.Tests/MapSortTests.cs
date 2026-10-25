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
            favorites: new HashSet<string>(StringComparer.Ordinal));

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
            favorites: new HashSet<string>(StringComparer.Ordinal));

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
            favorites: new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(["cs_office", "de_dust2", "de_inferno", "de_mirage"], result);
    }

    [Fact]
    public void Apply_Favorite_OrdersFavoritesFirst()
    {
        HashSet<string> favorites = new(StringComparer.Ordinal) { "de_inferno", "cs_office" };

        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "fav_sort", By = "favorite", Order = "desc" },
            _ => 0,
            favorites);

        Assert.Equal(["cs_office", "de_inferno", "de_dust2", "de_mirage"], result);
    }

    [Fact]
    public void Apply_Only_FiltersToFavorites()
    {
        HashSet<string> favorites = new(StringComparer.Ordinal) { "de_mirage" };

        List<string> result = MapSort.Apply(
            Catalog,
            Catalog,
            new SortConfig { Id = "favorites", By = "favorite", Order = "desc", Only = true },
            _ => 0,
            favorites);

        Assert.Equal(["de_mirage"], result);
    }

    [Fact]
    public void ChipEnabled_OnlyRequiresFavorites()
    {
        SortConfig only = new() { Id = "favorites", Only = true };
        Assert.False(MapSort.ChipEnabled(only, new HashSet<string>(StringComparer.Ordinal)));
        Assert.True(MapSort.ChipEnabled(only, new HashSet<string>(StringComparer.Ordinal) { "de_dust2" }));
        Assert.True(MapSort.ChipEnabled(
            new SortConfig { Id = "most_played", Only = false },
            new HashSet<string>(StringComparer.Ordinal)));
    }
}
