using MapVote.Voting;

namespace MapVote.Tests;

public class EngineVotePrepTests
{
    [Fact]
    public void BlankVoteSlots_SetsEveryEntryToEmptySentinel()
    {
        Span<int> slots = stackalloc int[] { 0, 3, 7, 1, 9 };
        EngineVotePrep.BlankVoteSlots(slots);
        Assert.All(slots.ToArray(), slot => Assert.Equal(EngineVotePrep.EmptyVoteSlot, slot));
    }
}
