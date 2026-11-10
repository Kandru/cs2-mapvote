namespace MapVote.Huds
{
    /// <summary>Layout contract: every panel id, dialog variable and ladder class spelled once.</summary>
    public static class VoteIds
    {
        public const string Root = "Vote";
        public const string Minimize = "mv-minimize";
        public const string VotedSection = "mv-voted-section";
        public const string VotedHint = "mv-voted-hint";
        public const string VotedStrip = "mv-voted-strip";
        public const string Current = "mv-current";
        public const string CurrentImage = "mv-current-image";
        public const string CurrentShot = "mv-current-shot";
        public const string CurrentRating = "mv-current-rating";
        public const string CurrentFav = "mv-current-fav";
        public const string CurrentLike = "mv-current-like";
        public const string CurrentDislike = "mv-current-dislike";
        public const string CurrentRatingVar = "mv_cur_rating";
        public const string CurrentNameVar = "mv_cur_name";
        public const string CurrentTagVar = "mv_current";
        public const string Filters = "mv-filters";
        public const string Empty = "mv-empty";
        public const string Catalog = "mv-catalog";
        public const string TitleVar = "mv_title";
        public const string HintVar = "mv_hint";
        public const string ChipVar = "mv_chip";
        public const string EmptyVar = "mv_empty";
        /// <summary>Favorites sort chip (next to All). Not an <c>mv-fav-*</c> tile star.</summary>
        public const string FavoritesSort = "mv-sort-fav";
        public const string FavoritesSortVar = "sfav";

        /// <summary>Grid panel pool (catalog max).</summary>
        public const int SlotCount = 100;
        /// <summary>Voted-strip pool; only the best-voted maps are shown.</summary>
        public const int VotedSlotCount = 10;
        public const int FilterTypeSlots = 8;
        public const int FilterSortSlots = 4;

        public static readonly string[] GridSlotIds = BuildIndexed("mv-slot-", SlotCount);
        public static readonly string[] GridTileIds = BuildIndexed("mv-", SlotCount);
        public static readonly string[] GridShotIds = BuildIndexed("mv-shot-", SlotCount);
        public static readonly string[] GridFavIds = BuildIndexed("mv-fav-", SlotCount);
        public static readonly string[] GridVoteVars = BuildIndexed("v", SlotCount);
        public static readonly string[] GridNameVars = BuildIndexed("n", SlotCount);
        public static readonly string[] GridRatingIds = BuildIndexed("mv-rate-", SlotCount);
        public static readonly string[] GridRatingVars = BuildIndexed("r", SlotCount);

        public static readonly string[] VotedSlotIds = BuildIndexed("mv-voted-slot-", VotedSlotCount);
        public static readonly string[] VotedTileIds = BuildIndexed("mv-voted-", VotedSlotCount);
        public static readonly string[] VotedShotIds = BuildIndexed("mv-voted-shot-", VotedSlotCount);
        public static readonly string[] VotedFavIds = BuildIndexed("mv-voted-fav-", VotedSlotCount);
        public static readonly string[] VotedVoteVars = BuildIndexed("vv", VotedSlotCount);
        public static readonly string[] VotedNameVars = BuildIndexed("vn", VotedSlotCount);
        public static readonly string[] VotedRatingIds = BuildIndexed("mv-voted-rate-", VotedSlotCount);
        public static readonly string[] VotedRatingVars = BuildIndexed("vr", VotedSlotCount);

        public static readonly string[] TypeChipIds = BuildIndexed("mv-ft-", FilterTypeSlots);
        public static readonly string[] TypeVars = BuildIndexed("ft", FilterTypeSlots);
        public static readonly string[] SortChipIds = BuildIndexed("mv-fs-", FilterSortSlots);
        public static readonly string[] SortVars = BuildIndexed("fs", FilterSortSlots);

        public static readonly string[] VwClasses = BuildLadder("vw-", VotedSlotCount);

        private static string[] BuildIndexed(string prefix, int count)
        {
            string[] ids = new string[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = prefix + i;
            }

            return ids;
        }

        private static string[] BuildLadder(string prefix, int maxInclusive)
        {
            string[] ids = new string[maxInclusive + 1];
            for (int i = 0; i <= maxInclusive; i++)
            {
                ids[i] = prefix + i;
            }

            return ids;
        }
    }
}
