using MapVote.Maps;

namespace MapVote.Tests;

public class CatalogRequestTests
{
    [Fact]
    public void Create_ClampsNegativeBudget()
    {
        CatalogRequest request = CatalogRequest.Create(-5, queryLocalMaps: true, queryMapgroup: false);

        Assert.Equal(0, request.BudgetSeconds);
        Assert.True(request.QueryLocalMaps);
        Assert.False(request.QueryMapgroup);
    }

    [Fact]
    public void Coalesce_KeepsLongestBudgetAndNewestSources()
    {
        CatalogRequest first = CatalogRequest.Create(1, queryLocalMaps: true, queryMapgroup: true);
        CatalogRequest second = CatalogRequest.Create(4, queryLocalMaps: false, queryMapgroup: true);

        CatalogRequest merged = first.Coalesce(second);

        Assert.Equal(4, merged.BudgetSeconds);
        Assert.False(merged.QueryLocalMaps);
        Assert.True(merged.QueryMapgroup);
    }

    [Fact]
    public void DescribeSources_NamesEnabledCombinations()
    {
        Assert.Equal("local+mapgroup", CatalogRequest.Create(0, true, true).DescribeSources());
        Assert.Equal("local", CatalogRequest.Create(0, true, false).DescribeSources());
        Assert.Equal("mapgroup", CatalogRequest.Create(0, false, true).DescribeSources());
        Assert.Equal("none", CatalogRequest.Create(0, false, false).DescribeSources());
    }
}
