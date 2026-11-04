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
    public void ResolveCustom_Panel3Vote20Reveal5_Opens11ChangesAt19()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 3,
            voteSeconds: 20,
            winnerRevealSeconds: 5);

        Assert.Equal(19, timing.ChangelevelOffsetSeconds);
        Assert.Equal(14, timing.RevealOffsetSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(11, timing.OpenSeconds);
    }

    [Fact]
    public void ResolveCustom_Panel0Vote20Reveal5_Opens14ChangesAt19()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 0,
            voteSeconds: 20,
            winnerRevealSeconds: 5);

        Assert.Equal(19, timing.ChangelevelOffsetSeconds);
        Assert.Equal(14, timing.RevealOffsetSeconds);
        Assert.Equal(5, timing.RevealSeconds);
        Assert.Equal(14, timing.OpenSeconds);
    }

    [Fact]
    public void CatalogTimeout_UsesPanelMinusOneWithFloorOfTwo()
    {
        Assert.Equal(2, VoteClock.CatalogTimeoutSeconds(0));
        Assert.Equal(2, VoteClock.CatalogTimeoutSeconds(-1));
        Assert.Equal(2, VoteClock.CatalogTimeoutSeconds(3));
        Assert.Equal(59, VoteClock.CatalogTimeoutSeconds(60));
    }

    [Fact]
    public void ResolveCustom_PanelConsumesDeadline_SkipsVote()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 19,
            voteSeconds: 20,
            winnerRevealSeconds: 5);

        Assert.Equal(19, timing.ChangelevelOffsetSeconds);
        Assert.Equal(19, timing.RevealOffsetSeconds);
        Assert.Equal(0, timing.OpenSeconds);
        Assert.Equal(0, timing.RevealSeconds);
    }

    [Fact]
    public void ResolveCustom_ShortPhase_ShrinksReveal()
    {
        VoteTiming timing = VoteClock.ResolveCustom(
            panelSeconds: 15,
            voteSeconds: 20,
            winnerRevealSeconds: 5);

        Assert.Equal(19, timing.ChangelevelOffsetSeconds);
        Assert.Equal(15, timing.RevealOffsetSeconds);
        Assert.Equal(4, timing.RevealSeconds);
        Assert.Equal(0, timing.OpenSeconds);
    }

    [Fact]
    public void ReadPanelSeconds_AllowsZeroAndFallsBack()
    {
        Assert.Equal(3, VoteClock.ReadPanelSeconds(_ => null));
        Assert.Equal(0, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? 0f : null));
        Assert.Equal(4, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? 3.2f : null));
        Assert.Equal(60, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? 60f : null));
        Assert.Equal(3, VoteClock.ReadPanelSeconds(name => name == "mp_win_panel_display_time" ? -1f : null));
    }
}
