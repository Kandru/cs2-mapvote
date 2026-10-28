using MapVote.Maps;

namespace MapVote.Tests;

public class MapNameFilterTests
{
    [Theory]
    [InlineData("de_dust2", "de_dust2")]
    [InlineData("DE_Basalt.vpk", "de_basalt")]
    [InlineData("cs_office.bsp", "cs_office")]
    public void NormalizeLocalLine_AcceptsMaps(string input, string expected)
    {
        Assert.Equal(expected, MapNameFilter.NormalizeLocalLine(input));
    }

    [Theory]
    [InlineData("maps/de_dust2")]
    [InlineData("lobby")]
    [InlineData("graphics_settings")]
    [InlineData("de_foo_vanity")]
    [InlineData("workshop")]
    [InlineData("")]
    [InlineData("bad name")]
    public void NormalizeLocalLine_RejectsJunk(string input)
    {
        Assert.Null(MapNameFilter.NormalizeLocalLine(input));
    }

    [Theory]
    [InlineData("de_safehouse", "de_safehouse")]
    [InlineData("MapGroup: mg_foo", null)]
    [InlineData("No maps in mapgroup map list", null)]
    public void NormalizeMapgroupLine(string input, string? expected)
    {
        Assert.Equal(expected, MapNameFilter.NormalizeMapgroupLine(input));
    }

    [Theory]
    [InlineData("lobby", MapLineKind.Skip)]
    [InlineData("maps/de_dust2", MapLineKind.Skip)]
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

    [Fact]
    public void SanitizeCssStem_FallsBackToDefault()
    {
        Assert.Equal("de_dust2", MapNameFilter.SanitizeCssStem("de_dust2"));
        Assert.Equal("default", MapNameFilter.SanitizeCssStem("Bad Name"));
    }
}
