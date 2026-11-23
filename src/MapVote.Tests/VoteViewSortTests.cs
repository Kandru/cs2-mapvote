using MapVote.Config;
using MapVote.Huds;

namespace MapVote.Tests;

public class VoteViewSortTests
{
    [Fact]
    public void DefaultSortIndex_FindsNameAscending()
    {
        List<SortConfig> sorts =
        [
            new() { Id = "newest", By = "newest", Order = "desc" },
            new() { Id = "alphabetical", By = "name", Order = "asc" },
        ];

        Assert.Equal(1, VoteView.DefaultSortIndex(sorts));
    }

    [Fact]
    public void DefaultSortIndex_RequiresAscendingNameSort()
    {
        List<SortConfig> sorts =
        [
            new() { Id = "z_a", By = "name", Order = "desc" },
        ];

        Assert.Equal(-1, VoteView.DefaultSortIndex(sorts));
    }

    [Fact]
    public void DefaultSortIndex_IgnoresSortsOutsideTheVisiblePool()
    {
        List<SortConfig> sorts = [];
        for (int i = 0; i < VoteIds.FilterSortSlots; i++)
        {
            sorts.Add(new SortConfig { Id = $"s{i}", By = "rating", Order = "desc" });
        }

        sorts.Add(new SortConfig { Id = "alphabetical", By = "name", Order = "asc" });

        Assert.Equal(-1, VoteView.DefaultSortIndex(sorts));
    }
}
