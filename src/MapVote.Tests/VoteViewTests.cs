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

    [Theory]
    [InlineData(5, 0, "p100")]
    [InlineData(0, 4, "p0")]
    [InlineData(3, 1, "p80")]   // 75 % rounds up
    [InlineData(99, 1, "p90")]  // mixed: never all green
    [InlineData(1, 99, "p10")]  // mixed: never all red
    public void RatingStep_MapsLikeShareToLadderClass(int likes, int dislikes, string expected)
    {
        Assert.Equal(expected, VoteIds.ShareClasses[VoteView.RatingStep(likes, dislikes)]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void RatingStep_UnratedHasNoLadderClass(int likes, int dislikes)
    {
        Assert.Equal(-1, VoteView.RatingStep(likes, dislikes));
    }

    [Theory]
    [InlineData("dd.MM.yyyy", "21.11.2026")]
    [InlineData("yyyy-MM-dd", "2026-11-21")]
    public void FormatAdded_UsesTheLanguageFormatOnTheUtcDay(string format, string expected)
    {
        long lateEvening = new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.Equal(expected, VoteView.FormatAdded(lateEvening, format));
    }

    [Fact]
    public void FormatAdded_EmptyWhenUnknownAndIsoWhenFormatBroken()
    {
        Assert.Equal(string.Empty, VoteView.FormatAdded(0, "dd.MM.yyyy"));
        Assert.Equal(string.Empty, VoteView.FormatAdded(-5, "dd.MM.yyyy"));
        Assert.Equal(string.Empty, VoteView.FormatAdded(long.MaxValue, "dd.MM.yyyy"));
        long day = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.Equal("2026-11-21", VoteView.FormatAdded(day, "%"));
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
    public void AvailableFavoriteCount_CountsOnlyFavoritesInCurrentMapList()
    {
        HashSet<string> favorites = new(StringComparer.Ordinal) { "de_inferno", "cs_office", "de_dust2" };

        Assert.Equal(1, VoteView.AvailableFavoriteCount(favorites, ["de_inferno"]));
        Assert.Equal(2, VoteView.AvailableFavoriteCount(favorites, ["de_inferno", "cs_office"]));
        Assert.Equal(0, VoteView.AvailableFavoriteCount(favorites, ["de_train"]));
    }

    [Fact]
    public void AvailableFavoriteCount_IsZeroForEmptyFavoritesOrMapList()
    {
        HashSet<string> favorites = new(StringComparer.Ordinal) { "de_inferno" };

        Assert.Equal(0, VoteView.AvailableFavoriteCount(new HashSet<string>(StringComparer.Ordinal), ["de_inferno"]));
        Assert.Equal(0, VoteView.AvailableFavoriteCount(favorites, []));
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
