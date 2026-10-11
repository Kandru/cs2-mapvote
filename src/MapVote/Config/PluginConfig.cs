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
}
