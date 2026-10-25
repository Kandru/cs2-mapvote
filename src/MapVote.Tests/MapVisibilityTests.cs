using MapVote.Maps;

namespace MapVote.Tests;

public class MapVisibilityTests
{
    [Fact]
    public void KeyFor_UsesMapAndWsPrefixes()
    {
        Assert.Equal("map_de_dust2", MapVisibility.KeyFor(new MapEntry("de_dust2", MapOrigin.Local)));
        Assert.Equal("ws_aim_redline", MapVisibility.KeyFor(new MapEntry("aim_redline", MapOrigin.Workshop)));
    }

    [Fact]
    public void TrySync_AddsMissingDiscoveredAsTrueAndSorts()
    {
        Dictionary<string, bool> votable = new(StringComparer.Ordinal)
        {
            ["ws_zzz"] = false,
            ["map_de_dust2"] = false,
        };

        List<MapEntry> discovered =
        [
            new("de_ancient", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
            new("de_dust2", MapOrigin.Local),
        ];

        Assert.True(MapVisibility.TrySync(votable, discovered));

        Assert.Equal(
            ["map_de_ancient", "map_de_dust2", "ws_aim_redline", "ws_zzz"],
            votable.Keys.ToList());
        Assert.True(votable["map_de_ancient"]);
        Assert.False(votable["map_de_dust2"]);
        Assert.True(votable["ws_aim_redline"]);
        Assert.False(votable["ws_zzz"]);
    }

    [Fact]
    public void TrySync_KeepsMissingServerMaps()
    {
        Dictionary<string, bool> votable = new(StringComparer.Ordinal)
        {
            ["map_de_inferno"] = false,
        };

        Assert.True(MapVisibility.TrySync(votable, [new MapEntry("de_dust2", MapOrigin.Local)]));
        Assert.False(votable["map_de_inferno"]);
        Assert.True(votable["map_de_dust2"]);
    }

    [Fact]
    public void TrySync_SecondPassIsNoOp()
    {
        Dictionary<string, bool> votable = new(StringComparer.Ordinal);
        List<MapEntry> discovered =
        [
            new("de_dust2", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
        ];

        Assert.True(MapVisibility.TrySync(votable, discovered));
        Assert.False(MapVisibility.TrySync(votable, discovered));
    }

    [Fact]
    public void Shown_HidesExplicitFalseAndKeepsOrder()
    {
        List<MapEntry> catalog =
        [
            new("de_dust2", MapOrigin.Local),
            new("de_mirage", MapOrigin.Local),
            new("aim_redline", MapOrigin.Workshop),
        ];
        Dictionary<string, bool> votable = new(StringComparer.Ordinal)
        {
            ["map_de_mirage"] = false,
        };

        List<MapEntry> shown = MapVisibility.Shown(catalog, votable);

        Assert.Equal(2, shown.Count);
        Assert.Equal("de_dust2", shown[0].Name);
        Assert.Equal("aim_redline", shown[1].Name);
    }

    [Fact]
    public void Shown_MissingKeyCountsAsShown()
    {
        List<MapEntry> catalog = [new("de_dust2", MapOrigin.Local)];
        List<MapEntry> shown = MapVisibility.Shown(catalog, new Dictionary<string, bool>(StringComparer.Ordinal));
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
            new Dictionary<string, bool>(StringComparer.Ordinal),
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
        Dictionary<string, bool> allHidden = new(StringComparer.Ordinal)
        {
            ["map_de_dust2"] = false,
            ["map_de_mirage"] = false,
        };

        Assert.False(MapVisibility.AnyShown(catalog, allHidden));
        Assert.True(MapVisibility.AnyShown(catalog, new Dictionary<string, bool>(StringComparer.Ordinal)));
    }

    [Fact]
    public void TrySync_AddedKeyAlreadyInOrderDoesNotRebuildUnnecessarily()
    {
        Dictionary<string, bool> votable = new(StringComparer.Ordinal)
        {
            ["map_de_ancient"] = true,
        };

        Assert.True(MapVisibility.TrySync(votable, [new MapEntry("de_dust2", MapOrigin.Local)]));
        Assert.Equal(["map_de_ancient", "map_de_dust2"], votable.Keys.ToList());
        Assert.False(MapVisibility.TrySync(votable, [new MapEntry("de_dust2", MapOrigin.Local)]));
    }
}
