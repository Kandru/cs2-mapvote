using System.Text.Json.Serialization;

namespace MapVote.Config
{
    /// <summary>
    /// Per-map settings in the config <c>maps</c> section. Keys are <c>map_{name}</c>
    /// for local maps and <c>ws_{name}</c> for workshop maps.
    /// </summary>
    public sealed class MapSettings
    {
        /// <summary>Whether the map is offered in the vote and browser. Missing key = shown.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Unix timestamp in UTC seconds when the map was first discovered. 0 = unknown.</summary>
        [JsonPropertyName("added")]
        public long Added { get; set; }
    }
}
