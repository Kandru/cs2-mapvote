using System.Text;
using System.Text.RegularExpressions;
using MapVote.Config;

namespace MapVote.Maps
{
    /// <summary>
    /// Type filter patterns from config <c>prefixes</c>.
    /// No <c>*</c>: prefix match. With <c>*</c>: full-name glob (<c>*</c> = any sequence).
    /// </summary>
    public static class MapTypeFilter
    {
        public static bool Matches(string map, string pattern)
        {
            if (pattern.Length == 0)
            {
                return false;
            }

            if (!pattern.Contains('*', StringComparison.Ordinal))
            {
                return map.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
            }

            return Regex.IsMatch(
                map,
                ToRegex(pattern),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static bool MatchesType(string map, FilterTypeConfig type, IReadOnlyList<FilterTypeConfig> all)
        {
            if (type.Prefixes.Count == 0)
            {
                // Empty list = "other": maps not claimed by any earlier typed entry.
                foreach (FilterTypeConfig earlier in all)
                {
                    if (ReferenceEquals(earlier, type))
                    {
                        break;
                    }

                    if (earlier.Prefixes.Count > 0 && MatchesAny(map, earlier.Prefixes))
                    {
                        return false;
                    }
                }

                return true;
            }

            return MatchesAny(map, type.Prefixes);
        }

        private static bool MatchesAny(string map, IReadOnlyList<string> patterns)
        {
            foreach (string pattern in patterns)
            {
                if (Matches(map, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string ToRegex(string pattern)
        {
            StringBuilder sb = new(pattern.Length * 2 + 2);
            sb.Append('^');
            foreach (char c in pattern)
            {
                sb.Append(c == '*' ? ".*" : Regex.Escape(c.ToString()));
            }

            sb.Append('$');
            return sb.ToString();
        }
    }
}
