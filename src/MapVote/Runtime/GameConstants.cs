namespace MapVote.Runtime
{
    /// <summary>
    /// Tuned values shared by the plugin features. Anything that is part of an external
    /// contract (Panorama ids, config keys, protocol limits) stays with its owning type.
    /// </summary>
    public static class GameConstants
    {
        // End-match timeline, measured from the intermission or win-panel epoch.
        public const int EngineChangelevelSlackSeconds = 1;
        public const int WinPanelDelaySeconds = 3;
        public const int MatchResultsSeconds = 6;
        public const int CatalogBudgetSeconds = 1;
        public const int DecisionOffsetSeconds = 2;
        public const int DefaultVoteSeconds = 20;

        // Catalog worker.
        public const int BrowseCatalogBudgetSeconds = 3;
        public const int CatalogRetryPauseMilliseconds = 400;
        public const int MaxCommandTimeoutMilliseconds = 3000;

        // HUD.
        public const int PaintWriteBudget = 128;
        public const int SpawnsPerTick = 1;
        public const int UrgentSeconds = 5;
        public const float HudOpenDelaySeconds = 0.2f;
        public const float JoinerOpenDelaySeconds = 0.5f;
        public const float CaptureDelaySeconds = 0.5f;
        public const float CloseTransmitGraceSeconds = 0.5f;

        // Voting rules.
        public static readonly TimeSpan VoteChangeCooldown = TimeSpan.FromSeconds(1);
    }
}
