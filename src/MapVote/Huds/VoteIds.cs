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
        public const string Filters = "mv-filters";
        public const string Empty = "mv-empty";
        public const string Catalog = "mv-catalog";
        /// <summary>Favorites sort chip (next to All). Not an <c>mv-fav-*</c> tile star.</summary>
        public const string FavoritesSort = "mv-sort-fav";
        public const string FavoritesSortVar = "sfav";

        public const int SlotCount = 100;
        public const int CatalogCols = 10;
        public const int CatalogRowCount = 10;
        public const int FilterTypeSlots = 8;
        public const int FilterSortSlots = 4;

        public static readonly string[] CrowIds = BuildIndexed("mv-crow-", CatalogRowCount);
        public static readonly string[] GridSlotIds = BuildIndexed("mv-slot-", SlotCount);
        public static readonly string[] GridTileIds = BuildIndexed("mv-", SlotCount);
        public static readonly string[] GridShotIds = BuildIndexed("mv-shot-", SlotCount);
        public static readonly string[] GridFavIds = BuildIndexed("mv-fav-", SlotCount);
        public static readonly string[] GridVoteVars = BuildIndexed("v", SlotCount);
        public static readonly string[] GridNameVars = BuildIndexed("n", SlotCount);

        public static readonly string[] VotedSlotIds = BuildIndexed("mv-voted-slot-", SlotCount);
        public static readonly string[] VotedTileIds = BuildIndexed("mv-voted-", SlotCount);
        public static readonly string[] VotedShotIds = BuildIndexed("mv-voted-shot-", SlotCount);
        public static readonly string[] VotedFavIds = BuildIndexed("mv-voted-fav-", SlotCount);
        public static readonly string[] VotedVoteVars = BuildIndexed("vv", SlotCount);
        public static readonly string[] VotedNameVars = BuildIndexed("vn", SlotCount);

        public static readonly string[] TypeChipIds = BuildIndexed("mv-ft-", FilterTypeSlots);
        public static readonly string[] TypeVars = BuildIndexed("ft", FilterTypeSlots);
        public static readonly string[] SortChipIds = BuildIndexed("mv-fs-", FilterSortSlots);
        public static readonly string[] SortVars = BuildIndexed("fs", FilterSortSlots);

        public static readonly string[] VwClasses = BuildLadder("vw-", SlotCount);
        public static readonly string[] RowsClasses = BuildLadder("rows-", CatalogRowCount);

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
