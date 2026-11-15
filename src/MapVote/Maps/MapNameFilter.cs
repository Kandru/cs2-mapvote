using System.Text.RegularExpressions;

namespace MapVote.Maps
{
    public enum MapLineKind
    {
        Accept,
        Skip,
        Ignore,
    }

    /// <summary>
    /// Pure filters for RCON <c>maps *</c> / mapgroup lines.
    /// Both sources share one playability check so workshop mapgroups cannot leak
    /// preview, vanity, skybox or engine helper entries into the vote pool.
    /// </summary>
    public static partial class MapNameFilter
    {
        private static readonly string[] DeniedPrefixes =
        [
            "workshop_preview_",
        ];

        private static readonly string[] DeniedSuffixes =
        [
            "_vanity",
            "_skybox",
        ];

        [GeneratedRegex("^[a-z0-9][a-z0-9_]{0,48}$", RegexOptions.CultureInvariant)]
        private static partial Regex ValidMapName();

        /// <summary>
        /// Manual mapgroups reference workshop maps as <c>workshop/&lt;id&gt;/&lt;name&gt;</c>.
        /// The id is retained so a numeric <c>Server.MapName</c> can still be resolved.
        /// </summary>
        [GeneratedRegex(
            @"^workshop[/\\](?<id>[0-9]+)[/\\](?<name>[^/\\]+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex WorkshopMapPath();

        /// <summary>
        /// True for a normalized, playable map id. Rejects paths, engine helper
        /// entries, workshop previews and vanity/skybox background maps.
        /// </summary>
        public static bool IsPlayableMapId(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            return IsPlayableNormalized(id);
        }

        /// <summary>
        /// Normalizes a raw map line or slug: trims, lowercases and strips
        /// <c>.vpk</c>/<c>.bsp</c>. Returns false for paths and non-playable ids.
        /// </summary>
        public static bool TryNormalizePlayableMapId(string? raw, out string id)
        {
            id = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string trimmed = raw.Trim();
            if (trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                return false;
            }

            string lower = trimmed.ToLowerInvariant();
            if (lower.EndsWith(".vpk", StringComparison.Ordinal)
                || lower.EndsWith(".bsp", StringComparison.Ordinal))
            {
                lower = Path.GetFileNameWithoutExtension(lower);
            }

            if (!IsPlayableNormalized(lower))
            {
                return false;
            }

            id = lower;
            return true;
        }

        /// <summary>
        /// Extracts the map name from a manual mapgroup workshop path
        /// (<c>workshop/&lt;id&gt;/&lt;name&gt;</c>). The path must contain a numeric
        /// workshop id, but no other path shape is accepted.
        /// </summary>
        public static bool TryParseWorkshopMapPath(string? raw, out string id, out string workshopId)
        {
            id = string.Empty;
            workshopId = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string trimmed = raw.Trim();
            if (!trimmed.Contains('/') && !trimmed.Contains('\\'))
            {
                return false;
            }

            Match match = WorkshopMapPath().Match(trimmed);
            if (!match.Success
                || !TryNormalizePlayableMapId(match.Groups["name"].Value, out id))
            {
                return false;
            }

            workshopId = match.Groups["id"].Value;
            return true;
        }

        /// <summary>
        /// Normalizes a current-map reference. A workshop path also identifies its origin;
        /// a bare map id stays local until the catalog lookup decides otherwise.
        /// </summary>
        public static bool TryNormalizeMapReference(string? raw, out string id, out MapOrigin origin)
        {
            id = string.Empty;
            origin = MapOrigin.Local;
            if (TryParseWorkshopMapPath(raw, out id, out _))
            {
                origin = MapOrigin.Workshop;
                return true;
            }

            return TryNormalizePlayableMapId(raw, out id);
        }

        public static bool IsValidMapName(string mapName) => IsPlayableMapId(mapName);

        public static string? NormalizeLocalLine(string line) =>
            ClassifyLocalLine(line, out string? name) == MapLineKind.Accept ? name : null;

        public static string? NormalizeMapgroupLine(string line) =>
            ClassifyMapgroupLine(line, out string? name) == MapLineKind.Accept ? name : null;

        /// <summary>
        /// Blank → Ignore. Paths, denied ids, invalid names → Skip. Valid map → Accept.
        /// </summary>
        public static MapLineKind ClassifyLocalLine(string line, out string? name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return MapLineKind.Ignore;
            }

            if (!TryNormalizePlayableMapId(line, out string normalized))
            {
                return MapLineKind.Skip;
            }

            name = normalized;
            return MapLineKind.Accept;
        }

        /// <summary>
        /// Blank, mapgroup headers (<c>:</c>), and empty-list messages → Ignore.
        /// <c>workshop/&lt;id&gt;/&lt;name&gt;</c> paths and valid bare ids → Accept.
        /// Other paths, denied ids and invalid names → Skip.
        /// </summary>
        public static MapLineKind ClassifyMapgroupLine(string line, out string? name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return MapLineKind.Ignore;
            }

            string trimmed = line.Trim();
            if (trimmed.Contains(':'))
            {
                return MapLineKind.Ignore;
            }

            if (trimmed.Contains("No maps in mapgroup map list", StringComparison.OrdinalIgnoreCase))
            {
                return MapLineKind.Ignore;
            }

            if (TryParseWorkshopMapPath(trimmed, out string workshopName, out _))
            {
                name = workshopName;
                return MapLineKind.Accept;
            }

            if (!TryNormalizePlayableMapId(trimmed, out string normalized))
            {
                return MapLineKind.Skip;
            }

            name = normalized;
            return MapLineKind.Accept;
        }

        private static bool IsPlayableNormalized(string id)
        {
            if (id.Length == 0 || id.Contains('/') || id.Contains('\\'))
            {
                return false;
            }

            foreach (string prefix in DeniedPrefixes)
            {
                if (id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            foreach (string suffix in DeniedSuffixes)
            {
                if (id.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (id is "error" or "graphics_settings" or "lobby" or "lobby_mapveto")
            {
                return false;
            }

            return ValidMapName().IsMatch(id);
        }
    }
}
