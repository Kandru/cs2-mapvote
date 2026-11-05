using MapVote.Voting;

namespace MapVote.Tests;

public class EndMatchFlowTests
{
    [Fact]
    public void CatalogBeforeDeadline_WithMaps_TakesCustomPath()
    {
        EndMatchFlow flow = new();
        Assert.True(flow.TryBegin());

        Assert.True(flow.TryDecide(3, out PathChoice choice));

        Assert.True(choice.Custom);
        Assert.Equal(EndMatchPhase.Ready, flow.Phase);
        Assert.True(flow.WatchNativeSlots);
    }

    [Fact]
    public void EmptyList_ArmsNative()
    {
        EndMatchFlow flow = new();
        Assert.True(flow.TryBegin());

        Assert.True(flow.TryDecide(0, out PathChoice choice));

        Assert.False(choice.Custom);
        Assert.Equal(EndMatchPhase.Native, flow.Phase);
        Assert.False(flow.WatchNativeSlots);
    }

    [Fact]
    public void DeadlineWithNoCatalog_ArmsNative()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();

        Assert.True(flow.TryDecide(0, out PathChoice choice));

        Assert.False(choice.Custom);
        Assert.Equal(EndMatchPhase.Native, flow.Phase);
    }

    [Fact]
    public void CallbackAfterDecision_DoesNotFlipPath()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();
        flow.TryDecide(0, out _);

        Assert.False(flow.TryDecide(4, out _));
        Assert.Equal(EndMatchPhase.Native, flow.Phase);

        EndMatchFlow custom = new();
        custom.TryBegin();
        custom.TryDecide(2, out _);

        Assert.False(custom.TryDecide(0, out _));
        Assert.Equal(EndMatchPhase.Ready, custom.Phase);
    }

    [Fact]
    public void WinPanelBeforeDecision_UsesTheListInHand()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();

        Assert.True(flow.TryBeginHold(2, out bool decidedNow, out PathChoice choice));

        Assert.True(decidedNow);
        Assert.True(choice.Custom);
        Assert.Equal(EndMatchPhase.Holding, flow.Phase);
    }

    [Fact]
    public void WinPanelOnNativePath_DoesNotOpenHud()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();
        flow.TryDecide(0, out _);

        Assert.False(flow.TryBeginHold(5, out bool decidedNow, out _));
        Assert.False(decidedNow);
        Assert.Equal(EndMatchPhase.Native, flow.Phase);
    }

    [Fact]
    public void SecondIntermission_IsIgnored()
    {
        EndMatchFlow flow = new();
        Assert.True(flow.TryBegin());
        Assert.False(flow.TryBegin());
        Assert.Equal(EndMatchPhase.Preparing, flow.Phase);
    }

    [Fact]
    public void Changelevel_IsOnce()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();
        flow.TryDecide(1, out _);
        Assert.True(flow.TryBeginHold(1, out _, out _));
        Assert.True(flow.TryBeginVoting());
        Assert.True(flow.TryBeginReveal());
        Assert.True(flow.TryBeginChange());
        Assert.False(flow.TryBeginChange());
        Assert.Equal(EndMatchPhase.Changing, flow.Phase);
    }

    [Fact]
    public void NewMatch_AfterNative_ReturnsToIdle()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();
        flow.TryDecide(0, out _);

        Assert.True(flow.TryFinishForNewMatch());
        Assert.Equal(EndMatchPhase.Idle, flow.Phase);
    }

    [Fact]
    public void NewMatch_DuringVote_StaysClosed()
    {
        EndMatchFlow flow = new();
        flow.TryBegin();
        flow.TryDecide(1, out _);
        flow.TryBeginHold(1, out _, out _);
        flow.TryBeginVoting();

        Assert.False(flow.TryFinishForNewMatch());
        Assert.Equal(EndMatchPhase.Voting, flow.Phase);
    }
}
