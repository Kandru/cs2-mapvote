using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Tests;

public class MapVisibilityTests
{
    private const long Now = 1_763_856_000;

    private static MapSettings Setting(bool enabled = true, long added = Now) =>
        new() { Enabled = enabled, Added = added };

    [Fact]
    public void KeyFor_UsesMapAndWsPrefixes()
    {
        Assert.Equal("map_de_dust2", MapVisibility.KeyFor(new MapEntry("de_dust2", MapOrigin.Local)));
        Assert.Equal("ws_aim_redline", MapVisibility.KeyFor(new MapEntry("aim_redline", MapOrigin.Workshop)));
    }

    [Fact]
    public void TrySync_AddsMissingDiscoveredAsEnabledAndSorts()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["ws_zzz"] = Setting(enabled: false),
            ["map_de_dust2"] = Setting(enabled: false),
        };

        List<MapEntry> discovered =
        [
            new("de_ancient", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
            new("de_dust2", MapOrigin.Local),
        ];

        Assert.True(MapVisibility.TrySync(maps, discovered, Now, prune: false));

        Assert.Equal(
            ["map_de_ancient", "map_de_dust2", "ws_aim_redline", "ws_zzz"],
            maps.Keys.ToList());
        Assert.True(maps["map_de_ancient"].Enabled);
        Assert.Equal(Now, maps["map_de_ancient"].Added);
        Assert.False(maps["map_de_dust2"].Enabled);
        Assert.True(maps["ws_aim_redline"].Enabled);
        Assert.False(maps["ws_zzz"].Enabled);
    }

    [Fact]
    public void TrySync_BackfillsMissingTimestampWithoutOverwritingIt()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_dust2"] = Setting(added: 0),
            ["map_de_mirage"] = Setting(added: 123),
        };

        Assert.True(MapVisibility.TrySync(
            maps,
            [new MapEntry("de_dust2", MapOrigin.Local), new MapEntry("de_mirage", MapOrigin.Local)],
            Now,
            prune: false));

        Assert.Equal(Now, maps["map_de_dust2"].Added);
        Assert.Equal(123, maps["map_de_mirage"].Added);
    }

    [Fact]
    public void TrySync_KeepsStaleEntriesWithoutPrune()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_inferno"] = Setting(enabled: false),
        };

        Assert.True(MapVisibility.TrySync(maps, [new MapEntry("de_dust2", MapOrigin.Local)], Now, prune: false));
        Assert.False(maps["map_de_inferno"].Enabled);
        Assert.True(maps["map_de_dust2"].Enabled);
    }

    [Fact]
    public void TrySync_PrunesUnknownEntriesOnlyWhenRequested()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_inferno"] = Setting(enabled: false),
            ["map_de_dust2"] = Setting(),
        };

        Assert.True(MapVisibility.TrySync(maps, [new MapEntry("de_dust2", MapOrigin.Local)], Now, prune: true));

        Assert.Equal(["map_de_dust2"], maps.Keys.ToList());
    }

    [Fact]
    public void TrySync_DoesNotPruneFromAnEmptyCatalog()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_inferno"] = Setting(),
        };

        Assert.False(MapVisibility.TrySync(maps, [], Now, prune: true));
        Assert.Single(maps);
    }

    [Fact]
    public void TrySync_SecondPassIsNoOp()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal);
        List<MapEntry> discovered =
        [
            new("de_dust2", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
        ];

        Assert.True(MapVisibility.TrySync(maps, discovered, Now, prune: true));
        Assert.False(MapVisibility.TrySync(maps, discovered, Now, prune: true));
    }

    [Fact]
    public void Shown_HidesExplicitlyDisabledAndKeepsOrder()
    {
        List<MapEntry> catalog =
        [
            new("de_dust2", MapOrigin.Local),
            new("de_mirage", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
        ];
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_mirage"] = Setting(enabled: false),
        };

        List<MapEntry> shown = MapVisibility.Shown(catalog, maps);

        Assert.Equal(2, shown.Count);
        Assert.Equal("de_dust2", shown[0].Name);
        Assert.Equal("aim_redline", shown[1].Name);
    }

    [Fact]
    public void Shown_MissingKeyCountsAsShown()
    {
        List<MapEntry> catalog = [new("de_dust2", MapOrigin.Local)];
        List<MapEntry> shown = MapVisibility.Shown(catalog, new Dictionary<string, MapSettings>(StringComparer.Ordinal));
        Assert.Single(shown);
        Assert.Equal("de_dust2", shown[0].Name);
    }

    [Fact]
    public void Shown_ExcludeNameSkipsMatch()
    {
        List<MapEntry> catalog =
        [
            new("de_dust2", MapOrigin.Local),
            new("de_mirage", MapOrigin.Local),
        ];

        List<MapEntry> shown = MapVisibility.Shown(
            catalog,
            new Dictionary<string, MapSettings>(StringComparer.Ordinal),
            excludeName: "de_dust2");

        Assert.Single(shown);
        Assert.Equal("de_mirage", shown[0].Name);
    }

    [Fact]
    public void AnyShown_EarlyExitsOnFirstEnabled()
    {
        List<MapEntry> catalog =
        [
            new("de_dust2", MapOrigin.Local),
            new("de_mirage", MapOrigin.Local),
        ];
        Dictionary<string, MapSettings> allHidden = new(StringComparer.Ordinal)
        {
            ["map_de_dust2"] = Setting(enabled: false),
            ["map_de_mirage"] = Setting(enabled: false),
        };

        Assert.False(MapVisibility.AnyShown(catalog, allHidden));
        Assert.True(MapVisibility.AnyShown(catalog, new Dictionary<string, MapSettings>(StringComparer.Ordinal)));
    }

    [Fact]
    public void TrySync_AddedKeyAlreadyInOrderDoesNotRebuildUnnecessarily()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_ancient"] = Setting(),
        };

        Assert.True(MapVisibility.TrySync(maps, [new MapEntry("de_dust2", MapOrigin.Local)], Now, prune: false));
        Assert.Equal(["map_de_ancient", "map_de_dust2"], maps.Keys.ToList());
        Assert.False(MapVisibility.TrySync(maps, [new MapEntry("de_dust2", MapOrigin.Local)], Now, prune: false));
    }

    [Fact]
    public void AddedFor_ResolvesByNameAndOrigin()
    {
        Dictionary<string, MapSettings> maps = new(StringComparer.Ordinal)
        {
            ["map_de_dust2"] = Setting(added: 42),
            ["ws_de_dust2"] = Setting(added: 99),
        };

        Assert.Equal(42, MapVisibility.AddedFor("de_dust2", MapOrigin.Local, maps));
        Assert.Equal(99, MapVisibility.AddedFor("de_dust2", MapOrigin.Workshop, maps));
        Assert.Equal(0, MapVisibility.AddedFor("de_mirage", MapOrigin.Local, maps));
    }
}
