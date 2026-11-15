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
        public static bool IsLocalReady(string response) =>
            MapCatalogParser.ParseLocalMaps(response).Ready;

        /// <summary>
        /// Empty/header-only without maps → not ready. Ready when the reply contains at least
        /// one accepted map, a workshop path, or the explicit empty-mapgroup message.
        /// </summary>
        public static bool IsMapgroupReady(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            return MapGroupList.Parse(response).Ready;
        }
    }
}
