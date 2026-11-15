namespace MapVote.Huds
{
    /// <summary>
    /// Layout v2 contract (tools/generate_vote_xml_v2.py docstring): every panel id, dialog
    /// variable and ladder class spelled once. Each viewer has a private layout entity, so
    /// every value is written on the viewer's own entity.
    /// </summary>
    public static class VoteIds
    {
        public const string Root = "Vote";

        // Top bar
        public const string TitleVar = "mv_title";
        public const string Timer = "mv-timer";                 // is-urgent
        public const string TimeVar = "mv_time";
        public const string TimerFill = "mv-timer-fill";        // span-N then is-running
        public const string TimerFillReveal = "mv-timer-fill-reveal"; // same, reveal only (fresh panel)
        public const string Minimize = "mv-minimize";
        public const string HideVar = "mv_hide";

        // Status
        public const string Status = "mv-status";               // is-self
        public const string StatusVar = "mv_status";

        // Current map + rating
        public const string Current = "mv-current";             // is-off (XML off)
        public const string CurrentTagVar = "mv_cur_tag";
        public const string CurrentNameVar = "mv_cur_name";
        public const string CurrentShot = "mv-current-shot";    // mv-map-*
        public const string CurrentFav = "mv-current-fav";      // is-active
        public const string Rate = "mv-rate";                   // is-disabled
        public const string RateQuestionVar = "mv_rate_q";
        public const string Like = "mv-like";                   // is-active
        public const string Dislike = "mv-dislike";             // is-active
        public const string LikeVar = "mv_like";
        public const string DislikeVar = "mv_dislike";
        public const string LikeCountVar = "mv_like_n";
        public const string DislikeCountVar = "mv_dislike_n";

        // Ranking
        public const string RankTitleVar = "mv_rank_title";
        public const string RankEmpty = "mv-rank-empty";        // is-off (XML off)
        public const string RankEmptyVar = "mv_rank_empty";
        public const string SelfVar = "mv_self";

        // Filters
        public const string Filters = "mv-filters";             // is-off (XML off)
        public const string FavOnly = "mv-fav-only";            // is-active, is-disabled
        public const string FavOnlyVar = "mv_favonly";

        // Grid
        public const string Empty = "mv-empty";                 // is-off (XML on)
        public const string EmptyVar = "mv_empty";

        // Reveal
        public const string WinTagVar = "mv_win_tag";
        public const string WinShot = "mv-win-shot";            // mv-map-*
        public const string WinNameVar = "mv_win_name";
        public const string WinVotesVar = "mv_win_votes";

        /// <summary>Grid panel pool (catalog max).</summary>
        public const int SlotCount = 100;
        public const int RankSlots = 6;
        public const int FilterTypeSlots = 8;
        public const int FilterSortSlots = 4;
        /// <summary>Generated countdown ladder span-3 … span-90.</summary>
        public const int SpanMin = 3;
        public const int SpanMax = 90;

        public static readonly string[] Views = ["view-vote", "view-reveal", "view-browse"];
        public static readonly string[] VotesClasses = ["votes-none", "votes-some"];

        public static readonly string[] GridSlotIds = BuildIndexed("mv-slot-", SlotCount);   // is-off (XML on), is-self
        public static readonly string[] GridTileIds = BuildIndexed("mv-", SlotCount);        // votes-none|votes-some
        public static readonly string[] GridShotIds = BuildIndexed("mv-shot-", SlotCount);
        public static readonly string[] GridFavIds = BuildIndexed("mv-fav-", SlotCount);     // is-active
        public static readonly string[] GridVoteVars = BuildIndexed("v", SlotCount);
        public static readonly string[] GridNameVars = BuildIndexed("n", SlotCount);

        public static readonly string[] RankRowIds = BuildIndexed("mv-rank-", RankSlots);        // is-off (XML on)
        public static readonly string[] RankShotIds = BuildIndexed("mv-rank-shot-", RankSlots);
        public static readonly string[] RankFillIds = BuildIndexed("mv-rank-fill-", RankSlots);  // p0…p100 (XML p0)
        public static readonly string[] RankSelfIds = BuildIndexed("mv-rank-self-", RankSlots);  // is-self
        public static readonly string[] RankNameVars = BuildIndexed("rn", RankSlots);
        public static readonly string[] RankVoteVars = BuildIndexed("rv", RankSlots);

        public static readonly string[] TypeChipIds = BuildIndexed("mv-ft-", FilterTypeSlots);  // is-off, is-active, is-disabled
        public static readonly string[] TypeVars = BuildIndexed("ft", FilterTypeSlots);
        public static readonly string[] SortChipIds = BuildIndexed("mv-fs-", FilterSortSlots);  // is-off, is-active
        public static readonly string[] SortVars = BuildIndexed("fs", FilterSortSlots);

        /// <summary>Share ladder p0, p10 … p100.</summary>
        public static readonly string[] ShareClasses = BuildIndexed("p", 11, step: 10);
        public static readonly string[] SpanClasses = BuildIndexed("span-", SpanMax + 1);

        private static string[] BuildIndexed(string prefix, int count, int step = 1)
        {
            string[] ids = new string[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = prefix + (i * step);
            }

            return ids;
        }
    }
}
