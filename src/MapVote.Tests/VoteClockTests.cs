using MapVote.Voting;

namespace MapVote.Tests;

public class VoteClockTests
{
    [Fact]
    public void ReadVoteSeconds_RoundsUpAndFallsBack()
    {
        Assert.Equal(20, VoteClock.ReadVoteSeconds(_ => null));
        Assert.Equal(1, VoteClock.ReadVoteSeconds(_ => null, fallback: 1));
        Assert.Equal(16, VoteClock.ReadVoteSeconds(name => name == "mp_endmatch_votenextleveltime" ? 15.2f : null));
        Assert.Equal(20, VoteClock.ReadVoteSeconds(name => name == "mp_endmatch_votenextleveltime" ? 20f : null));
        Assert.Equal(20, VoteClock.ReadVoteSeconds(name => name == "mp_endmatch_votenextleveltime" ? 0f : null));
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

    [Fact]
    public void ResolveCustom_Vote20Reveal5_Opens9ChangesAt19()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            voteSeconds: 20,
            winnerRevealSeconds: 5);

        Assert.Equal(19, timing.ChangelevelOffsetSeconds);
        Assert.Equal(14, timing.RevealOffsetSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(9, timing.OpenSeconds);
    }

    [Fact]
    public void CatalogBudget_IsOneSecond_DecisionByTwo()
    {
        Assert.Equal(1, VoteClock.CatalogBudgetSeconds);
        Assert.Equal(1, VoteClock.CatalogTimeoutSeconds());
        Assert.Equal(2, VoteClock.DecisionOffsetSeconds);
        Assert.Equal(5, VoteClock.MatchResultsSeconds);
    }

    [Fact]
    public void ResolveCustom_MatchResultsConsumesDeadline_SkipsVote()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            voteSeconds: 6,
            winnerRevealSeconds: 5);

        Assert.Equal(5, timing.ChangelevelOffsetSeconds);
        Assert.Equal(5, timing.RevealOffsetSeconds);
        Assert.Equal(0, timing.OpenSeconds);
        Assert.Equal(0, timing.RevealSeconds);
    }

    [Fact]
    public void ResolveCustom_ShortPhase_ShrinksReveal()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            voteSeconds: 10,
            winnerRevealSeconds: 5);

        Assert.Equal(9, timing.ChangelevelOffsetSeconds);
        Assert.Equal(5, timing.RevealOffsetSeconds);
        Assert.Equal(4, timing.RevealSeconds);
        Assert.Equal(0, timing.OpenSeconds);
    }
}
