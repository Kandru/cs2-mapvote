using MapVote.Config;

namespace MapVote.Tests;

public class ConfigMigrationTests
{
    private static FiltersConfig Legacy()
    {
        return new FiltersConfig
        {
            Sorts =
            [
                new SortConfig { Id = "most_played", By = "plays", Order = "desc" },
                new SortConfig { Id = "least_played", By = "plays", Order = "asc" },
                new SortConfig { Id = "most_liked", By = "rating", Order = "desc" },
                new SortConfig { Id = "least_liked", By = "rating", Order = "asc" },
                new SortConfig { Id = "favorites", By = "favorite", Order = "desc" },
            ],
        };
    }

    [Fact]
    public void EnsureSortDefaults_ReplacesLegacyDefaults()
    {
        FiltersConfig filters = Legacy();

        Assert.True(ConfigMigration.EnsureSortDefaults(filters));
        Assert.Equal(
            ["alphabetical", "newest", "oldest", "most_positive", "most_negative"],
            filters.Sorts.Select(static sort => sort.Id));
    }

    [Fact]
    public void EnsureSortDefaults_UpgradesPreviousDefaultSet()
    {
        FiltersConfig filters = new()
        {
            Sorts =
            [
                new SortConfig { Id = "alphabetical", By = "name", Order = "asc" },
                new SortConfig { Id = "most_positive", By = "rating", Order = "desc" },
                new SortConfig { Id = "most_negative", By = "rating", Order = "asc" },
            ],
        };

        Assert.True(ConfigMigration.EnsureSortDefaults(filters));
        Assert.Equal(
            ["alphabetical", "newest", "oldest", "most_positive", "most_negative"],
            filters.Sorts.Select(static sort => sort.Id));
    }

    [Fact]
    public void EnsureSortDefaults_IsIdempotent()
    {
        FiltersConfig filters = Legacy();

        Assert.True(ConfigMigration.EnsureSortDefaults(filters));
        Assert.False(ConfigMigration.EnsureSortDefaults(filters));
    }

    [Fact]
    public void EnsureSortDefaults_KeepsCustomLists()
    {
        FiltersConfig filters = new()
        {
            Sorts =
            [
                new SortConfig { Id = "by_plays", By = "plays", Order = "desc" },
                new SortConfig { Id = "favorites", By = "favorite", Order = "desc" },
            ],
        };

        Assert.False(ConfigMigration.EnsureSortDefaults(filters));
        Assert.Equal(["by_plays", "favorites"], filters.Sorts.Select(static sort => sort.Id));
    }

    [Fact]
    public void EnsureSortDefaults_LeavesEmptyList()
    {
        FiltersConfig filters = new() { Sorts = [] };

        Assert.False(ConfigMigration.EnsureSortDefaults(filters));
        Assert.Empty(filters.Sorts);
    }
}
