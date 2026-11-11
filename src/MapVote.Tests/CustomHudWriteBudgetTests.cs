using MapVote.Utils;

namespace MapVote.Tests;

public class CustomHudWriteBudgetTests
{
    [Fact]
    public void WritesInTick_KeepsCountWithinTheSameTick()
    {
        Assert.Equal(42, CustomHud.WritesInTick(countedTick: 1000, writes: 42, nowTick: 1000));
    }

    [Fact]
    public void WritesInTick_StartsAtZeroInAnyOtherTick()
    {
        Assert.Equal(0, CustomHud.WritesInTick(countedTick: 1000, writes: 128, nowTick: 1001));
        Assert.Equal(0, CustomHud.WritesInTick(countedTick: 1000, writes: 128, nowTick: 5));
        Assert.Equal(0, CustomHud.WritesInTick(countedTick: -1, writes: 0, nowTick: 0));
    }
}
