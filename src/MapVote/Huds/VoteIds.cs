namespace MapVote.Huds
{
    /// <summary>
    /// Layout v2 contract (tools/generate_vote_xml_v2.py docstring): every panel id, dialog
    /// variable and ladder class spelled once. G = written globally, P = per player.
    /// </summary>
    public static class VoteIds
    {
        public const string Root = "Vote";

        // Top bar
        public const string TitleVar = "mv_title";              // G
        public const string Timer = "mv-timer";                 // is-urgent P
        public const string TimeVar = "mv_time";                // G
        public const string TimerFill = "mv-timer-fill";        // span-N then is-running, P
        public const string TimerFillReveal = "mv-timer-fill-reveal"; // same, reveal only (fresh panel), P
        public const string Minimize = "mv-minimize";
        public const string HideVar = "mv_hide";                // P

        // Status
        public const string Status = "mv-status";               // is-self P
        public const string StatusVar = "mv_status";            // P

        // Current map + rating
        public const string Current = "mv-current";             // is-off G (XML off)
        public const string CurrentTagVar = "mv_cur_tag";       // G
        public const string CurrentNameVar = "mv_cur_name";     // G
        public const string CurrentShot = "mv-current-shot";    // mv-map-* G
        public const string CurrentFav = "mv-current-fav";      // is-active P
        public const string Rate = "mv-rate";                   // is-disabled P
        public const string RateQuestionVar = "mv_rate_q";      // G
        public const string Like = "mv-like";                   // is-active P
        public const string Dislike = "mv-dislike";             // is-active P
        public const string LikeVar = "mv_like";                // G
        public const string DislikeVar = "mv_dislike";          // G
        public const string LikeCountVar = "mv_like_n";         // G
        public const string DislikeCountVar = "mv_dislike_n";   // G

        // Ranking
        public const string RankTitleVar = "mv_rank_title";     // G
        public const string RankEmpty = "mv-rank-empty";        // is-off G (XML off)
        public const string RankEmptyVar = "mv_rank_empty";     // G
        public const string SelfVar = "mv_self";                // G

        // Filters
        public const string Filters = "mv-filters";             // is-off G (XML off)
        public const string FavOnly = "mv-fav-only";            // is-active P, is-disabled P
        public const string FavOnlyVar = "mv_favonly";          // G

        // Grid
        public const string Empty = "mv-empty";                 // is-off P (XML on)
        public const string EmptyVar = "mv_empty";              // P

        // Reveal
        public const string WinTagVar = "mv_win_tag";           // G
        public const string WinShot = "mv-win-shot";            // mv-map-* G
        public const string WinNameVar = "mv_win_name";         // G
        public const string WinVotesVar = "mv_win_votes";       // G

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

        public static readonly string[] GridSlotIds = BuildIndexed("mv-slot-", SlotCount);   // is-off P (XML on), is-self P
        public static readonly string[] GridTileIds = BuildIndexed("mv-", SlotCount);        // votes-none|votes-some G
        public static readonly string[] GridShotIds = BuildIndexed("mv-shot-", SlotCount);   // G
        public static readonly string[] GridFavIds = BuildIndexed("mv-fav-", SlotCount);     // is-active P
        public static readonly string[] GridVoteVars = BuildIndexed("v", SlotCount);         // G
        public static readonly string[] GridNameVars = BuildIndexed("n", SlotCount);         // G

        public static readonly string[] RankRowIds = BuildIndexed("mv-rank-", RankSlots);        // is-off G (XML on)
        public static readonly string[] RankShotIds = BuildIndexed("mv-rank-shot-", RankSlots);  // G
        public static readonly string[] RankFillIds = BuildIndexed("mv-rank-fill-", RankSlots);  // p0…p100 G (XML p0)
        public static readonly string[] RankSelfIds = BuildIndexed("mv-rank-self-", RankSlots);  // is-self P
        public static readonly string[] RankNameVars = BuildIndexed("rn", RankSlots);            // G
        public static readonly string[] RankVoteVars = BuildIndexed("rv", RankSlots);            // G

        public static readonly string[] TypeChipIds = BuildIndexed("mv-ft-", FilterTypeSlots);  // is-off G, is-active P, is-disabled G
        public static readonly string[] TypeVars = BuildIndexed("ft", FilterTypeSlots);         // G
        public static readonly string[] SortChipIds = BuildIndexed("mv-fs-", FilterSortSlots);  // is-off G, is-active G
        public static readonly string[] SortVars = BuildIndexed("fs", FilterSortSlots);         // G

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
