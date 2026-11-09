using System.Text.Json.Serialization;

namespace MapVote.Players
{
    public sealed class PlayerState
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("favorites")]
        public List<string> Favorites { get; set; } = [];

        /// <summary>Maps this player likes. Never overlaps <see cref="Dislikes"/>.</summary>
        [JsonPropertyName("likes")]
        public List<string> Likes { get; set; } = [];

        /// <summary>Maps this player dislikes. Never overlaps <see cref="Likes"/>.</summary>
        [JsonPropertyName("dislikes")]
        public List<string> Dislikes { get; set; } = [];

        [JsonIgnore]
        public bool Dirty;
    }
}
