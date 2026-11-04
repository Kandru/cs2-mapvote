using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace MapVote.Config
{
    public sealed class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("debug")]
        public bool Debug { get; set; }

        [JsonPropertyName("endmatch")]
        public EndmatchConfig Endmatch { get; set; } = new();

        [JsonPropertyName("rtv")]
        public RtvConfig Rtv { get; set; } = new();

        [JsonPropertyName("filters")]
        public FiltersConfig Filters { get; set; } = new();

        // Keys: map_{name} (local) / ws_{name} (workshop). true = shown; false = hidden. Stale keys kept.
        [JsonPropertyName("maps_votable")]
        public Dictionary<string, bool> MapsVotable { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class EndmatchConfig
    {
        [JsonPropertyName("exclude_current_map")]
        public bool ExcludeCurrentMap { get; set; } = true;

        /// <summary>
        /// When set, written to <c>mp_endmatch_votenextleveltime</c> on the win-panel event.
        /// Null leaves the server cvar alone.
        /// </summary>
        [JsonPropertyName("vote_seconds")]
        public int? VoteSeconds { get; set; }

        /// <summary>
        /// When set, written to <c>mp_win_panel_display_time</c> on the win-panel event.
        /// Null leaves the server cvar alone.
        /// </summary>
        [JsonPropertyName("win_panel_display_time")]
        public float? WinPanelDisplayTime { get; set; }

        [JsonPropertyName("winner_reveal_seconds")]
        public int WinnerRevealSeconds { get; set; } = 5;
    }

    public sealed class RtvConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("ratio")]
        public float Ratio { get; set; } = 0.6f;

        /// <summary>
        /// Seconds the RTV window stays open after the first vote. 0 = no limit.
        /// </summary>
        [JsonPropertyName("duration_seconds")]
        public int DurationSeconds { get; set; } = 60;

        /// <summary>
        /// Seconds between chat reminders while RTV is open. 0 = no reminders.
        /// </summary>
        [JsonPropertyName("announce_seconds")]
        public int AnnounceSeconds { get; set; } = 15;

        [JsonPropertyName("success_command")]
        public string SuccessCommand { get; set; } = "mp_halftime 0; mp_maxrounds 1";
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
        public List<SortConfig> Sorts { get; set; } =
        [
            new() { Id = "most_played", By = "plays", Order = "desc" },
            new() { Id = "least_played", By = "plays", Order = "asc" },
            new() { Id = "favorites", By = "favorite", Order = "desc", Only = true },
        ];
    }

    public sealed class FilterTypeConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("prefixes")]
        public List<string> Prefixes { get; set; } = [];
    }

    public sealed class SortConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("by")]
        public string By { get; set; } = "name";

        [JsonPropertyName("order")]
        public string Order { get; set; } = "asc";

        [JsonPropertyName("only")]
        public bool Only { get; set; }
    }
}
