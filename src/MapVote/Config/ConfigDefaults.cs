using MapVote.Maps;

namespace MapVote.Config
{
    /// <summary>Factory for the stock filter/sort lists. One definition, used by config defaults and migration.</summary>
    public static class ConfigDefaults
    {
        public static List<FilterTypeConfig> CreateFilterTypes() =>
        [
            new() { Id = "bomb", Prefixes = ["de_"] },
            new() { Id = "hostage", Prefixes = ["cs_"] },
            new() { Id = "arms_race", Prefixes = ["ar_"] },
            new() { Id = "danger_zone", Prefixes = ["dz_"] },
            new() { Id = "other", Prefixes = [] },
        ];

        public static List<SortConfig> CreateSorts() =>
        [
            new() { Id = "newest", By = "newest", Order = "desc" },
            new() { Id = "oldest", By = "oldest", Order = "asc" },
            new() { Id = "alphabetical", By = "name", Order = "asc" },
            new() { Id = "most_positive", By = "rating", Order = "desc" },
            new() { Id = "most_negative", By = "rating", Order = "asc" },
        ];

        public static string DefaultSuccessCommand => "mp_halftime 0; mp_maxrounds 1";

        public static string DefaultDisplayOrder => MapOrder.Shuffle;
    }
}
