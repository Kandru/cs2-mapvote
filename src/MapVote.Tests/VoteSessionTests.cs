using MapVote.Voting;

namespace MapVote.Tests;

public class VoteSessionTests
{
    [Fact]
    public void Cast_SortsVotedStripByCount_KeepsGridCatalogOrder()
    {
        VoteSession session = new(["a", "b", "c"]);

        Assert.True(session.Cast(1, "b"));
        Assert.True(session.Cast(2, "c"));
        Assert.True(session.Cast(3, "b"));
        Assert.Equal(["b", "c"], session.VotedOrder());
        Assert.Equal(["a", "b", "c"], session.GridOrder());
        Assert.Equal(2, session.CountOf("b"));
        Assert.Equal(1, session.CountOf("c"));

        // Equal counts keep first-vote order.
        Assert.True(session.Cast(4, "c"));
        Assert.Equal(["b", "c"], session.VotedOrder());

        // Higher count moves to the front of the strip.
        Assert.True(session.Cast(5, "c"));
        Assert.Equal(["c", "b"], session.VotedOrder());

        // Player changes mind: drops empty maps from the strip; grid still lists all.
        Assert.True(session.Cast(1, "a"));
        Assert.True(session.Cast(3, "a"));
        Assert.Equal(0, session.CountOf("b"));
        Assert.Equal(["c", "a"], session.VotedOrder());
        Assert.Equal(["a", "b", "c"], session.GridOrder());
    }

    [Fact]
    public void Cast_SameMap_IsNoOp()
    {
        VoteSession session = new(["a", "b"]);
        Assert.True(session.Cast(1, "a"));
        Assert.False(session.Cast(1, "a"));
        Assert.Equal(1, session.CountOf("a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Cast_EmptyOrNullMap_ReturnsFalse(string? map)
    {
        VoteSession session = new(["a", "b"]);
        Assert.False(session.Cast(1, map!));
        Assert.Equal(0, session.CountOf("a"));
    }

    [Fact]
    public void RemovePlayer_DropsVoteAndOrderEntry()
    {
        VoteSession session = new(["a", "b"]);
        session.Cast(1, "a");
        session.Cast(2, "b");
        Assert.True(session.RemovePlayer(1));
        Assert.Equal(0, session.CountOf("a"));
        Assert.Equal(["b"], session.VotedOrder());
    }

    [Fact]
    public void Lock_TieBreaksByFirstVoteOrder()
    {
        VoteSession session = new(["a", "b", "c"]);
        session.Cast(1, "b");
        session.Cast(2, "c");
        session.Cast(3, "b");
        session.Cast(4, "c");
        session.Lock();
        Assert.False(session.IsOpen);
        Assert.Equal("b", session.Winner);
        Assert.False(session.Cast(5, "a"));
    }

    [Fact]
    public void Lock_NoVotes_PicksRandomFromList()
    {
        VoteSession session = new(["a", "b", "c"], new Random(42));
        session.Lock();
        Assert.Contains(session.Winner!, session.Maps);
    }

    [Fact]
    public void Constructor_DropsDuplicateNames()
    {
        VoteSession session = new(["a", "a", "b", ""]);
        Assert.Equal(["a", "b"], session.Maps);
    }

    [Fact]
    public void CopyVotesTo_CarriesVotesForOfferedMaps()
    {
        VoteSession nominations = new(["a", "b", "c"]);
        nominations.Cast(1, "a");
        nominations.Cast(2, "a");
        nominations.Cast(3, "c");

        VoteSession final = new(["a", "c", "d"]);
        int copied = nominations.CopyVotesTo(final);

        Assert.Equal(3, copied);
        Assert.Equal(2, final.CountOf("a"));
        Assert.Equal(1, final.CountOf("c"));
        Assert.Equal("a", final.VoteOf(1));
        Assert.Equal("a", final.VoteOf(2));
        Assert.Equal("c", final.VoteOf(3));
        Assert.Equal(0, final.CountOf("b"));
        Assert.Equal(["a", "c"], final.VotedOrder());
    }

    [Fact]
    public void CopyVotesTo_SkipsVotesForUnavailableMaps()
    {
        VoteSession nominations = new(["a", "b"]);
        nominations.Cast(1, "a");
        nominations.Cast(2, "b");

        VoteSession final = new(["b"]);
        int copied = nominations.CopyVotesTo(final);

        Assert.Equal(1, copied);
        Assert.Null(final.VoteOf(1));
        Assert.Equal("b", final.VoteOf(2));
    }
}
