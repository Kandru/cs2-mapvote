using MapVote.Maps;

namespace MapVote.Tests;

public class MapNameFilterTests
{
    public static TheoryData<string> JunkLines => new()
    {
        "maps/de_dust2",
        "workshop/123/de_dust2",
        "workshop_preview_123",
        "workshop_preview_de_dust2",
        "lobby",
        "lobby_mapveto",
        "graphics_settings",
        "error",
        "de_foo_vanity",
        "de_foo_skybox",
        "bad name",
        "de_dust2!",
        new string('a', 50),
    };

    [Theory]
    [InlineData("de_dust2", "de_dust2")]
    [InlineData("DE_Basalt.vpk", "de_basalt")]
    [InlineData("cs_office.bsp", "cs_office")]
    [InlineData("  de_mirage  ", "de_mirage")]
    public void NormalizeLocalLine_AcceptsMaps(string input, string expected)
    {
        Assert.Equal(expected, MapNameFilter.NormalizeLocalLine(input));
    }

    [Theory]
    [MemberData(nameof(JunkLines))]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeLocalLine_RejectsJunk(string input)
    {
        Assert.Null(MapNameFilter.NormalizeLocalLine(input));
    }

    [Theory]
    [MemberData(nameof(JunkLines))]
    public void NormalizeMapgroupLine_RejectsJunk(string input)
    {
        Assert.Null(MapNameFilter.NormalizeMapgroupLine(input));
    }

    [Theory]
    [InlineData("de_safehouse", "de_safehouse")]
    [InlineData("  DE_SAFEHOUSE.vpk  ", "de_safehouse")]
    [InlineData("MapGroup: mg_foo", null)]
    [InlineData("No maps in mapgroup map list", null)]
    public void NormalizeMapgroupLine(string input, string? expected)
    {
        Assert.Equal(expected, MapNameFilter.NormalizeMapgroupLine(input));
    }

    [Theory]
    [MemberData(nameof(JunkLines))]
    public void ClassifyLocalLine_SkipsJunk(string input)
    {
        Assert.Equal(MapLineKind.Skip, MapNameFilter.ClassifyLocalLine(input, out string? name));
        Assert.Null(name);
    }

    [Theory]
    [MemberData(nameof(JunkLines))]
    public void ClassifyMapgroupLine_SkipsJunk(string input)
    {
        Assert.Equal(MapLineKind.Skip, MapNameFilter.ClassifyMapgroupLine(input, out string? name));
        Assert.Null(name);
    }

    [Theory]
    [InlineData("lobby", MapLineKind.Skip)]
    [InlineData("maps/de_dust2", MapLineKind.Skip)]
    [InlineData("workshop_preview_1", MapLineKind.Skip)]
    [InlineData("de_foo_vanity", MapLineKind.Skip)]
    [InlineData("de_foo_skybox", MapLineKind.Skip)]
    [InlineData("bad name", MapLineKind.Skip)]
    [InlineData("", MapLineKind.Ignore)]
    [InlineData("   ", MapLineKind.Ignore)]
    [InlineData("de_dust2", MapLineKind.Accept)]
    public void ClassifyLocalLine(string input, MapLineKind expected)
    {
        MapLineKind kind = MapNameFilter.ClassifyLocalLine(input, out string? name);
        Assert.Equal(expected, kind);
        if (expected == MapLineKind.Accept)
        {
            Assert.Equal("de_dust2", name);
        }
        else
        {
            Assert.Null(name);
        }
    }

    [Theory]
    [InlineData("MapGroup: mg_foo", MapLineKind.Ignore)]
    [InlineData("No maps in mapgroup map list", MapLineKind.Ignore)]
    [InlineData("", MapLineKind.Ignore)]
    [InlineData("bad name", MapLineKind.Skip)]
    [InlineData("workshop_preview_42", MapLineKind.Skip)]
    [InlineData("de_safehouse", MapLineKind.Accept)]
    public void ClassifyMapgroupLine(string input, MapLineKind expected)
    {
        MapLineKind kind = MapNameFilter.ClassifyMapgroupLine(input, out string? name);
        Assert.Equal(expected, kind);
        if (expected == MapLineKind.Accept)
        {
            Assert.Equal("de_safehouse", name);
        }
        else
        {
            Assert.Null(name);
        }
    }

    [Theory]
    [InlineData("de_dust2", true)]
    [InlineData("aim_redline", true)]
    [InlineData("de_dust2_1", true)]
    [InlineData("DE_DUST2", false)]
    [InlineData("workshop_preview_1", false)]
    [InlineData("de_foo_vanity", false)]
    [InlineData("de_foo_skybox", false)]
    [InlineData("lobby_mapveto", false)]
    [InlineData("graphics_settings", false)]
    [InlineData("error", false)]
    [InlineData("maps/de_dust2", false)]
    public void IsPlayableMapId(string input, bool expected)
    {
        Assert.Equal(expected, MapNameFilter.IsPlayableMapId(input));
    }

}
