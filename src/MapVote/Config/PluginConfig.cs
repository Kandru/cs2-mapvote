using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using MapVote.Maps;

namespace MapVote.Config
{
    public sealed class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("debug")]
        public bool Debug { get; set; }

        /// <summary>
        /// Hide map-type prefixes such as <c>de_</c> / <c>cs_</c> in the HUD,
        /// capitalize the first letter and list maps by that cleaned name.
        /// </summary>
        [JsonPropertyName("clean_map_names")]
        public bool CleanMapNames { get; set; } = true;

        [JsonPropertyName("endmatch")]
        public EndmatchConfig Endmatch { get; set; } = new();

        [JsonPropertyName("rtv")]
        public RtvConfig Rtv { get; set; } = new();

        [JsonPropertyName("nominations")]
        public NominationsConfig Nominations { get; set; } = new();

        [JsonPropertyName("display")]
        public DisplayConfig Display { get; set; } = new();

        [JsonPropertyName("filters")]
        public FiltersConfig Filters { get; set; } = new();

        [JsonPropertyName("catalog")]
        public CatalogConfig Catalog { get; set; } = new();

        // Keys: map_{name} (local) / ws_{name} (workshop).
        [JsonPropertyName("maps")]
        public Dictionary<string, MapSettings> Maps { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Repairs every nullable section/list so the rest of the plugin can assume a complete
        /// config. Idempotent; explicit empty lists keep their meaning.
        /// </summary>
        public void Normalize()
        {
            Endmatch ??= new EndmatchConfig();
            Rtv ??= new RtvConfig();
            Nominations ??= new NominationsConfig();
            Display ??= new DisplayConfig();
            Filters ??= new FiltersConfig();
            Catalog ??= new CatalogConfig();
            Maps ??= new Dictionary<string, MapSettings>(StringComparer.Ordinal);

            Filters.Types = NormalizeTypes(Filters.Types);
            Filters.Sorts = NormalizeSorts(Filters.Sorts);
            NormalizeMaps(Maps);

            if (string.IsNullOrWhiteSpace(Display.Order))
            {
                Display.Order = ConfigDefaults.DefaultDisplayOrder;
            }

            if (string.IsNullOrWhiteSpace(Rtv.SuccessCommand))
            {
                Rtv.SuccessCommand = ConfigDefaults.DefaultSuccessCommand;
            }

            if (Endmatch.MaxMaps <= 0)
            {
                Endmatch.MaxMaps = MapCatalog.MaxMaps;
            }
            else
            {
                Endmatch.MaxMaps = Math.Min(Endmatch.MaxMaps, MapCatalog.MaxMaps);
            }

            Endmatch.WinnerRevealSeconds = Math.Max(1, Endmatch.WinnerRevealSeconds);
            Rtv.DurationSeconds = Math.Max(0, Rtv.DurationSeconds);
            Rtv.AnnounceSeconds = Math.Max(0, Rtv.AnnounceSeconds);
            Rtv.Ratio = float.IsFinite(Rtv.Ratio)
                ? Math.Clamp(Rtv.Ratio, 0.01f, 1f)
                : 0.6f;
        }

        private static void NormalizeMaps(Dictionary<string, MapSettings> settings)
        {
            foreach (string key in settings.Keys.ToList())
            {
                MapSettings? value = settings[key];
                if (value is null)
                {
                    settings[key] = new MapSettings();
                    continue;
                }

                value.Added = Math.Max(0, value.Added);
            }
        }

        private static List<FilterTypeConfig> NormalizeTypes(List<FilterTypeConfig>? configured)
        {
            if (configured is null)
            {
                return ConfigDefaults.CreateFilterTypes();
            }

            List<FilterTypeConfig> types = [];
            foreach (FilterTypeConfig? type in configured)
            {
                if (type is null)
                {
                    continue;
                }

                type.Id = type.Id?.Trim() ?? string.Empty;
                type.Prefixes ??= [];
                if (type.Id.Length > 0)
                {
                    types.Add(type);
                }
            }

            return types;
        }

        private static List<SortConfig> NormalizeSorts(List<SortConfig>? configured)
        {
            if (configured is null)
            {
                return ConfigDefaults.CreateSorts();
            }

            List<SortConfig> sorts = [];
            foreach (SortConfig? sort in configured)
            {
                if (sort is null)
                {
                    continue;
                }

                sort.Id = sort.Id?.Trim() ?? string.Empty;
                sort.By = string.IsNullOrWhiteSpace(sort.By) ? "name" : sort.By.Trim().ToLowerInvariant();
                sort.Order = string.IsNullOrWhiteSpace(sort.Order) ? "asc" : sort.Order.Trim().ToLowerInvariant();
                if (sort.Id.Length > 0)
                {
                    sorts.Add(sort);
                }
            }

            return sorts;
        }
    }

    /// <summary>RCON sources used to build the map catalog.</summary>
    public sealed class CatalogConfig
    {
        /// <summary>
        /// Query <c>maps *</c>. Those maps are always treated as local/internal maps.
        /// </summary>
        [JsonPropertyName("query_local_maps")]
        public bool QueryLocalMaps { get; set; } = true;

        /// <summary>
        /// Query <c>print_mapgroup_sv</c>. The reply identifies workshop maps by its
        /// <c>Map group: workshop</c> collection header or by <c>workshop/&lt;id&gt;/&lt;name&gt;</c> paths.
        /// </summary>
        [JsonPropertyName("query_mapgroup")]
        public bool QueryMapGroup { get; set; } = true;
    }

    public sealed class EndmatchConfig
    {
        [JsonPropertyName("exclude_current_map")]
        public bool ExcludeCurrentMap { get; set; } = true;

        /// <summary>
        /// Menu length in seconds. Null uses the server cvar, read once before the plugin stretches it.
        /// The engine timer is written to this length plus 4s so intermission does not change the map first.
        /// </summary>
        [JsonPropertyName("vote_seconds")]
        public int? VoteSeconds { get; set; }

        [JsonPropertyName("winner_reveal_seconds")]
        public int WinnerRevealSeconds { get; set; } = 5;

        [JsonPropertyName("max_maps")]
        public int MaxMaps { get; set; } = MapCatalog.MaxMaps;
    }

    public sealed class NominationsConfig
    {
        /// <summary>
        /// Allow picking a map through the <c>!maps</c> browser while the match is running.
        /// Nominated maps are guaranteed end-match vote candidates and the nominator's vote is prefilled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
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
        public string SuccessCommand { get; set; } = ConfigDefaults.DefaultSuccessCommand;
    }

    public sealed class FiltersConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("types")]
        public List<FilterTypeConfig> Types { get; set; } = ConfigDefaults.CreateFilterTypes();

        [JsonPropertyName("sorts")]
        public List<SortConfig> Sorts { get; set; } = ConfigDefaults.CreateSorts();
    }

    public sealed class DisplayConfig
    {
        /// <summary>
        /// Base order of the map list: <c>shuffle</c> (default, assigned once per match) or
        /// <c>alphabetical</c>. User sort chips order on top of this.
        /// </summary>
        [JsonPropertyName("order")]
        public string Order { get; set; } = ConfigDefaults.DefaultDisplayOrder;
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
    }
}
