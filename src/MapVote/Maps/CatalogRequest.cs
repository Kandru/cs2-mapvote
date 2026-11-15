namespace MapVote.Maps
{
    /// <summary>
    /// One catalog refresh request. Requests that arrive while the worker is busy are
    /// coalesced: the newest source selection wins and the longest budget is kept.
    /// </summary>
    public readonly record struct CatalogRequest(
        int BudgetSeconds,
        bool QueryLocalMaps,
        bool QueryMapgroup)
    {
        public static CatalogRequest Create(
            int budgetSeconds,
            bool queryLocalMaps,
            bool queryMapgroup) =>
            new(Math.Max(0, budgetSeconds), queryLocalMaps, queryMapgroup);

        public CatalogRequest Coalesce(CatalogRequest newer) =>
            Create(
                Math.Max(BudgetSeconds, newer.BudgetSeconds),
                newer.QueryLocalMaps,
                newer.QueryMapgroup);

        public string DescribeSources() =>
            (QueryLocalMaps, QueryMapgroup) switch
            {
                (true, true) => "local+mapgroup",
                (true, false) => "local",
                (false, true) => "mapgroup",
                _ => "none",
            };
    }
}
