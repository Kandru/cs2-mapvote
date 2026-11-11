using MapVote.Players;

namespace MapVote.Tests;

public sealed class PlayerArchivePersistenceTests : IDisposable
{
    private const string Alice = "76561198000000001";

    private readonly string _dir = Directory.CreateTempSubdirectory("mapvote-archive-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveIfDirty_WritesOnlyWhenDirtyAndClearsFlag()
    {
        PlayerArchive archive = new();
        string file = PlayerFiles.FilePath(_dir, Alice);

        Assert.False(archive.SaveIfDirty(_dir, Alice));
        archive.GetOrCreate(Alice);
        Assert.False(archive.SaveIfDirty(_dir, Alice));
        Assert.False(File.Exists(file));

        archive.SetRating(Alice, "de_dust2", PlayerRating.Like);
        Assert.True(archive.SaveIfDirty(_dir, Alice));
        Assert.True(File.Exists(file));
        Assert.False(archive.TryGet(Alice)!.Dirty);
        Assert.False(archive.SaveIfDirty(_dir, Alice));
    }

    [Fact]
    public void Load_KeepsDirtyStateAndRebuildsCounts()
    {
        PlayerArchive writer = new();
        writer.SetRating(Alice, "de_mirage", PlayerRating.Like);
        writer.SaveDirty(_dir);

        PlayerArchive archive = new();
        archive.Load(_dir);
        archive.SetRating(Alice, "de_mirage", PlayerRating.Dislike);

        archive.Load(_dir);

        Assert.True(archive.TryGet(Alice)!.Dirty);
        Assert.Equal(PlayerRating.Dislike, archive.RatingOf(Alice, "de_mirage"));
        Assert.Equal(0, archive.LikeCount("de_mirage"));
        Assert.Equal(1, archive.DislikeCount("de_mirage"));
    }

    [Fact]
    public void Load_ReplacesCleanStateWithFileContent()
    {
        PlayerArchive archive = new();
        archive.SetRating(Alice, "de_mirage", PlayerRating.Like);
        archive.SaveDirty(_dir);

        PlayerArchive external = new();
        external.SetRating(Alice, "de_inferno", PlayerRating.Dislike);
        external.SaveDirty(_dir);

        archive.Load(_dir);

        Assert.False(archive.TryGet(Alice)!.Dirty);
        Assert.Equal(PlayerRating.None, archive.RatingOf(Alice, "de_mirage"));
        Assert.Equal(PlayerRating.Dislike, archive.RatingOf(Alice, "de_inferno"));
        Assert.Equal(0, archive.LikeCount("de_mirage"));
    }
}
