using MapVote.Voting;

namespace MapVote.Tests;

public class RtvRuleTests
{
    [Theory]
    [InlineData(0, 0.6f, 1)]
    [InlineData(1, 0.6f, 1)]
    [InlineData(10, 0.6f, 6)]
    [InlineData(10, 0.55f, 6)]
    [InlineData(3, 0.6f, 2)]
    [InlineData(10, 0.01f, 1)]
    [InlineData(10, 1f, 10)]
    [InlineData(10, 2f, 10)]
    public void VotesNeeded_RoundsUpAndClamps(int humans, float ratio, int expected)
    {
        Assert.Equal(expected, RtvRule.VotesNeeded(humans, ratio));
    }

    [Theory]
    [InlineData(-1f, 0.01f)]
    [InlineData(0f, 0.01f)]
    [InlineData(0.6f, 0.6f)]
    [InlineData(1.5f, 1f)]
    public void ClampRatio_KeepsUsableRange(float ratio, float expected)
    {
        Assert.Equal(expected, RtvRule.ClampRatio(ratio));
    }

    [Theory]
    [InlineData(60, 15, 15)] // 45s left, then 30, 15
    [InlineData(62, 15, 2)]  // 60s left, then 45, 30, 15
    [InlineData(10, 15, null)] // no 0s reminder
    [InlineData(0, 15, 15)]
    [InlineData(60, 0, null)]
    public void NextAnnounceDelay_UsesWholeReminderBoundaries(int duration, int interval, int? expected)
    {
        Assert.Equal(expected, RtvRule.NextAnnounceDelay(duration, interval));
    }
}
