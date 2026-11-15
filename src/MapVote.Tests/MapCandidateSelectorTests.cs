using MapVote.Maps;

namespace MapVote.Tests;

public class MapCandidateSelectorTests
{
    private static readonly MapEntry Alpha = new("de_alpha", MapOrigin.Local);
    private static readonly MapEntry Beta = new("de_beta", MapOrigin.Local);
    private static readonly MapEntry Gamma = new("de_gamma", MapOrigin.Local);
    private static readonly MapEntry Workshop = new("123456", MapOrigin.Workshop);

    private static readonly MapEntry[] Catalog = [Alpha, Beta, Gamma, Workshop];

    [Fact]
    public void Select_DropsDisabledAndExcludedMaps()
    {
        Dictionary<string, bool> votable = new(StringComparer.Ordinal)
        {
            [MapVisibility.KeyFor(Beta)] = false,
        };

        List<MapEntry> maps = MapCandidateSelector.Select(Catalog, votable, Gamma.Name, 64);

        Assert.Equal([Alpha, Workshop], maps);
    }

    [Fact]
    public void Select_CapsWithoutLosingPriority()
    {
        int reportedFound = 0;
        int reportedLimit = 0;
        List<MapEntry> maps = MapCandidateSelector.Select(
            Catalog,
            new Dictionary<string, bool>(StringComparer.Ordinal),
            excludeName: null,
            limit: 2,
            priority: [Workshop.Name],
            onTruncated: (found, limit) =>
            {
                reportedFound = found;
                reportedLimit = limit;
            });

        Assert.Equal(2, maps.Count);
        Assert.Contains(Workshop, maps);
        Assert.Equal(4, reportedFound);
        Assert.Equal(2, reportedLimit);
    }

    [Fact]
    public void Select_DoesNotReportTruncationWhenWithinLimit()
    {
        bool reported = false;
        List<MapEntry> maps = MapCandidateSelector.Select(
            [Alpha, Beta],
            new Dictionary<string, bool>(StringComparer.Ordinal),
            excludeName: null,
            limit: 2,
            onTruncated: (_, _) => reported = true);

        Assert.Equal([Alpha, Beta], maps);
        Assert.False(reported);
    }

    [Fact]
    public void Select_ReturnsEmptyForEmptyCatalog()
    {
        Assert.Empty(MapCandidateSelector.Select(
            [],
            new Dictionary<string, bool>(StringComparer.Ordinal),
            excludeName: null,
            limit: 64));
    }

    [Fact]
    public void TakeInBaseOrder_KeepsSourceListWhenItAlreadyFits()
    {
        List<MapEntry> maps = [Beta, Alpha];

        Assert.Same(maps, MapCandidateSelector.TakeInBaseOrder(maps, [Alpha.Name], 5));
    }

    [Fact]
    public void Distinct_DropsEmptyAndDuplicateNamesButKeepsFirstAndOrder()
    {
        List<MapEntry> maps =
        [
            Beta,
            new(string.Empty, MapOrigin.Local),
            Alpha,
            new(Beta.Name, MapOrigin.Workshop),
            Gamma,
        ];

        Assert.Equal([Beta, Alpha, Gamma], MapCandidateSelector.Distinct(maps));
    }
}
