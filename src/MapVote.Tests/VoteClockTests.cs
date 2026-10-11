using MapVote.Voting;

namespace MapVote.Tests;

public class VoteClockTests
{
    [Fact]
    public void Resolve_UsesEndmatchCvarMinusReveal()
    {
        VoteTiming timing = VoteClock.Resolve(endmatchVoteNextLevelTime: 20f, matchRestartDelay: 5, fallbackVoteSeconds: 15, revealSeconds: 5);
        Assert.Equal(15, timing.OpenSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(20, timing.TotalSeconds);
    }

    [Fact]
    public void Resolve_FallsBackToRestartDelayWhenEndmatchTooShort()
    {
        VoteTiming timing = VoteClock.Resolve(endmatchVoteNextLevelTime: 3f, matchRestartDelay: 12, fallbackVoteSeconds: 15, revealSeconds: 5);
        Assert.Equal(7, timing.OpenSeconds);
        Assert.Equal(5, timing.RevealSeconds);
    }

    [Fact]
    public void Resolve_UsesFallbackWhenOthersMissing()
    {
        VoteTiming timing = VoteClock.Resolve(null, null, fallbackVoteSeconds: 15, revealSeconds: 5);
        Assert.Equal(10, timing.OpenSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(15, timing.TotalSeconds);
    }

    [Fact]
    public void Resolve_EnsuresAtLeastOneOpenSecond()
    {
        VoteTiming timing = VoteClock.Resolve(6f, null, 15, 5);
        Assert.Equal(1, timing.OpenSeconds);
        Assert.Equal(5, timing.RevealSeconds);
    }
}
