using MapVote.Config;
using MapVote.Voting;

namespace MapVote.Tests;

public class VoteClockTests
{
    [Fact]
    public void Resolve_UsesConfiguredWindows()
    {
        VoteTiming timing = VoteClock.Resolve(new EndmatchConfig
        {
            VoteSeconds = 15,
            WinnerRevealSeconds = 5,
        });
        Assert.Equal(15, timing.OpenSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(20, timing.TotalSeconds);
        Assert.Equal(21, timing.EngineHoldSeconds);
    }

    [Fact]
    public void Resolve_ClampsOpenAndRevealToAtLeastOne()
    {
        VoteTiming timing = VoteClock.Resolve(voteSeconds: 0, winnerRevealSeconds: 0);
        Assert.Equal(1, timing.OpenSeconds);
        Assert.Equal(1, timing.RevealSeconds);
        Assert.Equal(2, timing.TotalSeconds);
        Assert.Equal(3, timing.EngineHoldSeconds);
    }

    [Fact]
    public void Resolve_ClampsNegativeToOne()
    {
        VoteTiming timing = VoteClock.Resolve(voteSeconds: -3, winnerRevealSeconds: -1);
        Assert.Equal(1, timing.OpenSeconds);
        Assert.Equal(1, timing.RevealSeconds);
        Assert.Equal(3, timing.EngineHoldSeconds);
    }
}
