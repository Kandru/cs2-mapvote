using System.Globalization;
using System.Text.RegularExpressions;

namespace MapVote.Maps
{
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

        public static string? NormalizeLocalLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string trimmed = line.Trim();
            if (trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                return null;
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
                    return null;
                }
            }

            return ValidMapName().IsMatch(lower) ? lower : null;
        }

        public static string? NormalizeMapgroupLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string trimmed = line.Trim();
            if (trimmed.Contains(':'))
            {
                return null;
            }

            if (trimmed.Contains("No maps in mapgroup map list", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string lower = trimmed.ToLower(CultureInfo.InvariantCulture);
            return ValidMapName().IsMatch(lower) ? lower : null;
        }

        public static string SanitizeCssStem(string mapName)
        {
            string lower = mapName.Trim().ToLowerInvariant();
            return ValidMapName().IsMatch(lower) ? lower : "default";
        }
    }
}
