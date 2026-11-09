using MapVote.Players;

namespace MapVote.Tests;

public class PlayerRatingTests
{
    private const string Alice = "76561198000000001";
    private const string Bob = "76561198000000002";

    [Fact]
    public void SetRating_LikeThenDislike_SwitchesAndKeepsCountsExclusive()
    {
        PlayerArchive archive = new();

        Assert.True(archive.SetRating(Alice, "de_dust2", PlayerRating.Like));
        Assert.Equal(PlayerRating.Like, archive.RatingOf(Alice, "de_dust2"));
        Assert.Equal(1, archive.LikeCount("de_dust2"));
        Assert.Equal(0, archive.DislikeCount("de_dust2"));
        Assert.Equal(1, archive.RatingScore("de_dust2"));

        Assert.True(archive.SetRating(Alice, "de_dust2", PlayerRating.Dislike));
        Assert.Equal(PlayerRating.Dislike, archive.RatingOf(Alice, "de_dust2"));
        Assert.Equal(0, archive.LikeCount("de_dust2"));
        Assert.Equal(1, archive.DislikeCount("de_dust2"));
        Assert.Equal(-1, archive.RatingScore("de_dust2"));

        // Re-applying the same rating is a no-op and must not double-count.
        Assert.False(archive.SetRating(Alice, "de_dust2", PlayerRating.Dislike));
        Assert.Equal(1, archive.DislikeCount("de_dust2"));

        Assert.True(archive.SetRating(Alice, "de_dust2", PlayerRating.None));
        Assert.Equal(PlayerRating.None, archive.RatingOf(Alice, "de_dust2"));
        Assert.Equal(0, archive.RatingScore("de_dust2"));
    }

    [Fact]
    public void RatingScore_AggregatesAcrossPlayers()
    {
        PlayerArchive archive = new();
        archive.SetRating(Alice, "de_mirage", PlayerRating.Like);
        archive.SetRating(Bob, "de_mirage", PlayerRating.Like);
        archive.SetRating(Alice, "cs_office", PlayerRating.Dislike);
        archive.SetRating(Bob, "cs_office", PlayerRating.Like);

        Assert.Equal(2, archive.LikeCount("de_mirage"));
        Assert.Equal(2, archive.RatingTotal("de_mirage"));
        Assert.Equal(2, archive.RatingScore("de_mirage"));
        Assert.Equal(2, archive.RatingTotal("cs_office"));
        Assert.Equal(0, archive.RatingScore("cs_office"));
        Assert.Equal(0, archive.RatingTotal("de_dust2"));
        Assert.Equal(0, archive.RatingScore("de_dust2"));
    }

    [Fact]
    public void Serialize_RoundTripsLikesAndDislikes()
    {
        PlayerState original = new()
        {
            SteamId = Alice,
            Username = "Alice",
            Favorites = ["de_dust2"],
            Likes = ["de_mirage"],
            Dislikes = ["cs_office"],
        };

        PlayerState? restored = PlayerFiles.DeserializeBytes(PlayerFiles.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(new[] { "de_dust2" }, restored.Favorites);
        Assert.Equal(new[] { "de_mirage" }, restored.Likes);
        Assert.Equal(new[] { "cs_office" }, restored.Dislikes);
    }

    [Fact]
    public void Deserialize_MissingRatingListsBecomeEmpty()
    {
        byte[] json = """{"steamid":"76561198000000001","username":"Alice"}"""u8.ToArray();

        PlayerState? restored = PlayerFiles.DeserializeBytes(json);

        Assert.NotNull(restored);
        Assert.Empty(restored.Likes);
        Assert.Empty(restored.Dislikes);
    }
}
