using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace MapVote.Config
{
    public sealed class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("debug")]
        public bool Debug { get; set; }

        [JsonPropertyName("exclude_current_map")]
        public bool ExcludeCurrentMap { get; set; } = true;

        [JsonPropertyName("fallback_vote_seconds")]
        public int FallbackVoteSeconds { get; set; } = 15;

        [JsonPropertyName("reveal_seconds")]
        public int RevealSeconds { get; set; } = 5;

        [JsonPropertyName("catalog_max_age_seconds")]
        public int CatalogMaxAgeSeconds { get; set; } = 30;

        [JsonPropertyName("rtv")]
        public RtvConfig Rtv { get; set; } = new();

        [JsonPropertyName("filters")]
        public FiltersConfig Filters { get; set; } = new();

        [JsonPropertyName("persistence")]
        public PersistenceConfig Persistence { get; set; } = new();
    }

    public sealed class RtvConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("ratio")]
        public float Ratio { get; set; } = 0.6f;

        [JsonPropertyName("success_command")]
        public string SuccessCommand { get; set; } = "mp_halftime 0; mp_maxrounds 1";
    }

    public sealed class PersistenceConfig
    {
        [JsonPropertyName("autosave_on_round_end")]
        public bool AutosaveOnRoundEnd { get; set; } = true;

        [JsonPropertyName("autosave_per_tick")]
        public int AutosavePerTick { get; set; } = 2;
    }

    public sealed class FiltersConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("types")]
        public List<FilterTypeConfig> Types { get; set; } =
        [
            new() { Id = "bomb", Prefixes = ["de_"] },
            new() { Id = "hostage", Prefixes = ["cs_"] },
            new() { Id = "arms_race", Prefixes = ["ar_"] },
            new() { Id = "danger_zone", Prefixes = ["dz_"] },
            new() { Id = "other", Prefixes = [] },
        ];

        [JsonPropertyName("sorts")]
        public List<string> Sorts { get; set; } = ["most_played", "least_played", "favorites"];
    }

    public sealed class FilterTypeConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("prefixes")]
        public List<string> Prefixes { get; set; } = [];
    }
}
