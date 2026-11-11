using MapVote.Huds;
using MapVote.Players;

namespace MapVote.Tests;

public class VoteViewTests
{
    [Theory]
    [InlineData(12.4, 12)]
    [InlineData(12.5, 13)]
    [InlineData(1.0, 3)]    // below the ladder: shortest span
    [InlineData(-2.0, 3)]
    [InlineData(150.0, 90)] // above the ladder: longest span
    public void SpanFor_PicksNearestGeneratedSpan(double remaining, int expected)
    {
        Assert.Equal(expected, VoteView.SpanFor(remaining));
        Assert.Equal($"span-{expected}", VoteIds.SpanClasses[expected]);
    }

    [Theory]
    [InlineData(8.1, 9, "0:09")]
    [InlineData(8.0, 8, "0:08")]
    [InlineData(-0.5, 0, "0:00")]
    [InlineData(75.0, 75, "1:15")]
    public void Countdown_RoundsUpAndFormats(double remaining, int seconds, string text)
    {
        Assert.Equal(seconds, VoteView.SecondsLeft(remaining));
        Assert.Equal(text, VoteView.FormatTime(seconds));
    }

    [Theory]
    [InlineData(0, 0, "p0")]
    [InlineData(4, 9, "p40")]   // 44 %
    [InlineData(1, 8, "p10")]   // 12.5 %
    [InlineData(1, 20, "p10")]  // 5 % rounds up
    [InlineData(1, 30, "p0")]   // 3 % rounds down
    [InlineData(3, 3, "p100")]
    public void ShareStep_MapsToLadderClass(int votes, int total, string expected)
    {
        Assert.Equal(expected, VoteIds.ShareClasses[VoteView.ShareStep(votes, total)]);
    }

    [Fact]
    public void IsVisible_CombinesTypeChipAndFavoritesOnly()
    {
        const int bomb = 1 | (1 << 1); // All + chip 1
        Assert.True(VoteView.IsVisible(bomb, 0, false, false));
        Assert.True(VoteView.IsVisible(bomb, 1, false, false));
        Assert.False(VoteView.IsVisible(bomb, 2, false, true));
        Assert.False(VoteView.IsVisible(bomb, 1, true, false));
        Assert.True(VoteView.IsVisible(bomb, 1, true, true));
    }

    [Fact]
    public void RatingToggle_MovesAndRemovesCounts()
    {
        const string alice = "76561198000000001";
        PlayerArchive archive = new();

        void Click(PlayerRating clicked) =>
            archive.SetRating(alice, "de_dust2", VoteView.NextRating(archive.RatingOf(alice, "de_dust2"), clicked));

        Click(PlayerRating.Like);
        Assert.Equal((1, 0), (archive.LikeCount("de_dust2"), archive.DislikeCount("de_dust2")));

        Click(PlayerRating.Dislike); // switch moves the count
        Assert.Equal((0, 1), (archive.LikeCount("de_dust2"), archive.DislikeCount("de_dust2")));

        Click(PlayerRating.Dislike); // clicking the selected one removes it
        Assert.Equal((0, 0), (archive.LikeCount("de_dust2"), archive.DislikeCount("de_dust2")));
        Assert.Equal(PlayerRating.None, archive.RatingOf(alice, "de_dust2"));
    }
}
