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

        [JsonIgnore]
        public bool Dirty;
    }
}
