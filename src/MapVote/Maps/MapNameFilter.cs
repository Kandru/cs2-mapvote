using System.Globalization;
using System.Text.RegularExpressions;

namespace MapVote.Maps
{
    public enum MapLineKind
    {
        Accept,
        Skip,
        Ignore,
    }

    /// <summary>Pure filters for RCON <c>maps *</c> / mapgroup lines.</summary>
    public static partial class MapNameFilter
    {
        private static readonly string[] DeniedSubstrings =
        [
            "lobby",
            "error",
            "graphics_settings",
            "vanity",
            "preview",
            "workshop",
            "background",
        ];

        [GeneratedRegex("^[a-z0-9][a-z0-9_]{0,48}$", RegexOptions.CultureInvariant)]
        private static partial Regex ValidMapName();

        public static bool IsValidMapName(string mapName) =>
            !string.IsNullOrEmpty(mapName) && ValidMapName().IsMatch(mapName);

        public static string? NormalizeLocalLine(string line) =>
            ClassifyLocalLine(line, out string? name) == MapLineKind.Accept ? name : null;

        public static string? NormalizeMapgroupLine(string line) =>
            ClassifyMapgroupLine(line, out string? name) == MapLineKind.Accept ? name : null;

        /// <summary>
        /// Blank → Ignore. Paths, denied substrings, invalid names → Skip. Valid map → Accept.
        /// </summary>
        public static MapLineKind ClassifyLocalLine(string line, out string? name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return MapLineKind.Ignore;
            }

            string trimmed = line.Trim();
            if (trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                return MapLineKind.Skip;
            }

            string lower = trimmed.ToLowerInvariant();
            if (lower.EndsWith(".vpk", StringComparison.Ordinal) || lower.EndsWith(".bsp", StringComparison.Ordinal))
            {
                lower = Path.GetFileNameWithoutExtension(lower);
            }

            foreach (string denied in DeniedSubstrings)
            {
                if (lower.Contains(denied, StringComparison.Ordinal))
                {
                    return MapLineKind.Skip;
                }
            }

            if (!ValidMapName().IsMatch(lower))
            {
                return MapLineKind.Skip;
            }

            name = lower;
            return MapLineKind.Accept;
        }

        /// <summary>
        /// Blank, mapgroup headers (<c>:</c>), and empty-list messages → Ignore.
        /// Invalid names → Skip. Valid map → Accept.
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

            string lower = trimmed.ToLower(CultureInfo.InvariantCulture);
            if (!ValidMapName().IsMatch(lower))
            {
                return MapLineKind.Skip;
            }

            name = lower;
            return MapLineKind.Accept;
        }

        public static string SanitizeCssStem(string mapName)
        {
            string lower = mapName.Trim().ToLowerInvariant();
            return ValidMapName().IsMatch(lower) ? lower : "default";
        }
    }
}
