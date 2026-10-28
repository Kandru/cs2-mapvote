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

    [Fact]
    public void SecondsUntil_ReturnsRemainingFromEpoch()
    {
        DateTime epoch = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(15f, VoteClock.SecondsUntil(epoch, offsetSeconds: 15, nowUtc: epoch));
        Assert.Equal(10f, VoteClock.SecondsUntil(epoch, offsetSeconds: 15, nowUtc: epoch.AddSeconds(5)));
        Assert.Equal(0f, VoteClock.SecondsUntil(epoch, offsetSeconds: 15, nowUtc: epoch.AddSeconds(15)));
        Assert.Equal(-2f, VoteClock.SecondsUntil(epoch, offsetSeconds: 15, nowUtc: epoch.AddSeconds(17)));
    }
}
