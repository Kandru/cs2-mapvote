using MapVote.Maps;

namespace MapVote.Tests;

public class MapListReadinessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void IsLocalReady_BlankBody_IsNotReady(string? body)
    {
        Assert.False(MapListReadiness.IsLocalReady(body!));
    }

    [Fact]
    public void IsLocalReady_AcceptedMap_IsReady()
    {
        Assert.True(MapListReadiness.IsLocalReady("de_dust2\nde_mirage"));
    }

    [Fact]
    public void IsLocalReady_SkippedJunkLines_IsReady()
    {
        // Skip lines prove the command answered, even with no Accept.
        Assert.True(MapListReadiness.IsLocalReady("workshop/123/de_foo\nlobby"));
    }

    [Fact]
    public void IsLocalReady_SkippedWorkshopPath_IsReady()
    {
        // A path line is Skip (proves the command answered), even with no Accept.
        Assert.True(MapListReadiness.IsLocalReady("workshop/123/de_foo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMapgroupReady_BlankBody_IsNotReady(string? body)
    {
        Assert.False(MapListReadiness.IsMapgroupReady(body!));
    }

    [Fact]
    public void IsMapgroupReady_HeaderOnly_IsNotReady()
    {
        Assert.False(MapListReadiness.IsMapgroupReady("Map group: mg_active"));
    }

    [Fact]
    public void IsMapgroupReady_ExplicitEmptyList_IsReady()
    {
        Assert.True(MapListReadiness.IsMapgroupReady(
            "Map group: mg_active\nNo maps in mapgroup map list"));
    }

    [Fact]
    public void IsMapgroupReady_MapLines_IsReady()
    {
        Assert.True(MapListReadiness.IsMapgroupReady(
            "Map group: mg_active\n\tde_dust2\n\tde_mirage"));
    }
}
