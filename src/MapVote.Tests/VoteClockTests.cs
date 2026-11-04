using MapVote.Config;
using MapVote.Voting;

namespace MapVote.Tests;

public class VoteClockTests
{
    [Fact]
    public void NativeVoteSeconds_ClampsToAtLeastOne()
    {
        Assert.Equal(15, VoteClock.NativeVoteSeconds(new EndmatchConfig { VoteSeconds = 15 }));
        Assert.Equal(1, VoteClock.NativeVoteSeconds(0));
        Assert.Equal(1, VoteClock.NativeVoteSeconds(-3));
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
    public void ResolveCustom_Master60Query2Reveal5_Votes52ChangesAt59()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 60,
            winnerRevealSeconds: 5,
            queryElapsedSeconds: 2f);

        Assert.Equal(59, timing.ChangelevelOffsetSeconds);
        Assert.Equal(54, timing.RevealOffsetSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(52, timing.OpenSeconds);
    }

    [Fact]
    public void ResolveCustom_Master60Query8Reveal5_Votes46ChangesAt59()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 60,
            winnerRevealSeconds: 5,
            queryElapsedSeconds: 8f);

        Assert.Equal(59, timing.ChangelevelOffsetSeconds);
        Assert.Equal(54, timing.RevealOffsetSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(46, timing.OpenSeconds);
    }

    [Fact]
    public void QueryBudget_CapsAtPanelMinusRevealMinusSlackMinusOne()
    {
        Assert.Equal(8, VoteClock.QueryBudgetSeconds(60, catalogWaitSeconds: 8, winnerRevealSeconds: 5));
        Assert.Equal(3, VoteClock.QueryBudgetSeconds(10, catalogWaitSeconds: 8, winnerRevealSeconds: 5));
        Assert.Equal(0, VoteClock.QueryBudgetSeconds(3, catalogWaitSeconds: 8, winnerRevealSeconds: 5));
        Assert.Equal(0, VoteClock.QueryBudgetSeconds(60, catalogWaitSeconds: 0, winnerRevealSeconds: 5));
    }

    [Fact]
    public void ResolveCustom_QueryOverrunsReveal_SkipsVote()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 10,
            winnerRevealSeconds: 5,
            queryElapsedSeconds: 5f);

        Assert.Equal(9, timing.ChangelevelOffsetSeconds);
        Assert.Equal(5, timing.RevealOffsetSeconds);
        Assert.Equal(0, timing.OpenSeconds);
        Assert.Equal(4, timing.RevealSeconds);
    }

    [Fact]
    public void ResolveCustom_QueryPastChangelevel_ChangesImmediately()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 10,
            winnerRevealSeconds: 5,
            queryElapsedSeconds: 12f);

        Assert.Equal(9, timing.ChangelevelOffsetSeconds);
        Assert.Equal(9, timing.RevealOffsetSeconds);
        Assert.Equal(0, timing.OpenSeconds);
        Assert.Equal(0, timing.RevealSeconds);
    }

    [Fact]
    public void ReadPanelSeconds_RoundsUpAndFallsBack()
    {
        Assert.Equal(3, VoteClock.ReadPanelSeconds(_ => null));
        Assert.Equal(4, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? 3.2f : null));
        Assert.Equal(60, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? 60f : null));
    }
}
