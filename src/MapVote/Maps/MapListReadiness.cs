namespace MapVote.Maps
{
    /// <summary>
    /// Whether an RCON map-list reply looks complete enough to trust, or is still
    /// an empty body that should be retried.
    /// </summary>
    public static class MapListReadiness
    {
        /// <summary>
        /// Empty body → not ready. Ready when at least one map is accepted, or at least
        /// one skipped line (paths / denied names prove the command answered).
        /// </summary>
        public static bool IsLocalReady(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            foreach (string line in response.Split('\n'))
            {
                MapLineKind kind = MapNameFilter.ClassifyLocalLine(line, out _);
                if (kind is MapLineKind.Accept or MapLineKind.Skip)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Empty body → not ready. Ready when at least one map line is present, or the
        /// explicit empty-mapgroup message. Header-only with neither stays not ready.
        /// </summary>
        public static bool IsMapgroupReady(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            foreach (string line in response.Split('\n'))
            {
                if (line.Contains("No maps in mapgroup map list", StringComparison.OrdinalIgnoreCase)
                    || MapNameFilter.ClassifyMapgroupLine(line, out _) == MapLineKind.Accept)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
