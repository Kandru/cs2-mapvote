using MapVote.Runtime;

namespace MapVote.Tests;

public class PlayerSlotsTests
{
    [Theory]
    [InlineData(-1, 64, false)]
    [InlineData(0, 64, true)]
    [InlineData(63, 64, true)]
    [InlineData(64, 64, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]
    public void IsValid_UsesZeroBasedBounds(int slot, int capacity, bool expected)
    {
        Assert.Equal(expected, PlayerSlots.IsValid(slot, capacity));
    }

    [Fact]
    public void Capacity_IsPositive()
    {
        Assert.True(PlayerSlots.Capacity > 0);
    }
}
