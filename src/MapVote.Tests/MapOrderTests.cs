using MapVote.Maps;

namespace MapVote.Tests;

public class MapOrderTests
{
    private static readonly IReadOnlyList<string> Maps =
        ["de_dust2", "de_mirage", "cs_office", "de_inferno", "ar_shoots"];

    [Fact]
    public void Alphabetical_UsesCleanedDisplayName()
    {
        MapOrder.Configure(MapOrder.Alphabetical);

        List<string> result = MapOrder.Sort(Maps, MapNameDisplay.Format);

        Assert.Equal(["de_dust2", "de_inferno", "de_mirage", "cs_office", "ar_shoots"], result);
    }

    [Fact]
    public void Shuffle_IsStableWithinTheMatch()
    {
        MapOrder.Configure(MapOrder.Shuffle);
        MapOrder.ResetMatch();

        List<string> first = MapOrder.Sort(Maps, MapNameDisplay.Format);
        List<string> second = MapOrder.Sort(Maps, MapNameDisplay.Format);

        Assert.Equal(first, second);
        Assert.Equal(Maps.OrderBy(static m => m, StringComparer.Ordinal), first.OrderBy(static m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void Shuffle_NewMapsAreAppendedConsistently()
    {
        MapOrder.Configure(MapOrder.Shuffle);
        MapOrder.ResetMatch();

        List<string> first = MapOrder.Sort(Maps, MapNameDisplay.Format);
        List<string> withExtra = MapOrder.Sort([.. Maps, "de_nuke"], MapNameDisplay.Format);

        Assert.Equal(first, withExtra.Where(first.Contains));
        Assert.Contains("de_nuke", withExtra);
    }
}
