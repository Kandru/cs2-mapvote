using MapVote.Maps;
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

    [Theory]
    [InlineData(MapOrigin.Local, "de_dust2", "changelevel de_dust2")]
    [InlineData(MapOrigin.Workshop, "workshop_map", "ds_workshop_changelevel workshop_map")]
    public void ChangelevelCommand_UsesOrigin(MapOrigin origin, string map, string command)
    {
        Assert.Equal(command, EngineVotePrep.ChangelevelCommand(origin, map));
    }
}
