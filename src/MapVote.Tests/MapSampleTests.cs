using MapVote.Maps;

namespace MapVote.Tests;

public class MapSampleTests
{
    private static List<MapEntry> Maps(int count)
    {
        List<MapEntry> maps = new(count);
        for (int i = 0; i < count; i++)
        {
            maps.Add(new MapEntry($"de_map{i:000}", MapOrigin.Local));
        }

        return maps;
    }

    [Fact]
    public void Take_OverManySeeds_EveryMapIsSelectedAndResultsAreValid()
    {
        List<MapEntry> maps = Maps(150);
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int seed = 0; seed < 50; seed++)
        {
            List<MapEntry> result = MapSample.Take(maps, 100, new Random(seed));
            Assert.True(result.Count <= 100);
            Assert.Equal(result.Count, result.Select(m => m.Name).Distinct().Count());
            foreach (MapEntry map in result)
            {
                seen.Add(map.Name);
            }
        }

        Assert.Equal(150, seen.Count);
    }

    [Fact]
    public void Take_PreservesRelativeOrder()
    {
        List<MapEntry> maps = Maps(150);

        List<MapEntry> result = MapSample.Take(maps, 100, new Random(7));

        List<string> names = result.Select(m => m.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void Take_ReturnsExactlyLimit()
    {
        Assert.Equal(12, MapSample.Take(Maps(150), 12, new Random(1)).Count);
    }

    [Fact]
    public void Take_FittingListIsUnchanged()
    {
        List<MapEntry> maps = Maps(20);

        List<MapEntry> result = MapSample.Take(maps, 100, new Random(1));

        Assert.Equal(maps, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Take_LimitBelowOneReturnsOne(int limit)
    {
        Assert.Single(MapSample.Take(Maps(20), limit, new Random(1)));
    }
}
