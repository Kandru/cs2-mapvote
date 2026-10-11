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

    [Fact]
    public void SanitizeCssStem_FallsBackToDefault()
    {
        Assert.Equal("de_dust2", MapNameFilter.SanitizeCssStem("de_dust2"));
        Assert.Equal("default", MapNameFilter.SanitizeCssStem("Bad Name"));
    }
}
