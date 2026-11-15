using MapVote.Players;

namespace MapVote.Tests;

public sealed class PlayerArchiveIdentityTests : IDisposable
{
    private const string Alice = "76561198000000001";
    private const string Bob = "76561198000000002";

    private readonly string _dir = Directory.CreateTempSubdirectory("mapvote-identity-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_QuarantinesFileWithNonSteamIdName()
    {
        string path = Path.Combine(_dir, "not-a-steamid.json");
        PlayerFiles.Write(path, new PlayerState { SteamId = Alice, Username = "Alice" });

        PlayerArchive archive = new();
        archive.Load(_dir);

        Assert.Null(archive.TryGet(Alice));
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Load_QuarantinesContentIdMismatch()
    {
        string path = PlayerFiles.FilePath(_dir, Alice);
        PlayerFiles.Write(path, new PlayerState { SteamId = Bob, Username = "Alice" });

        PlayerArchive archive = new();
        archive.Load(_dir);

        Assert.Null(archive.TryGet(Alice));
        Assert.Null(archive.TryGet(Bob));
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Load_AcceptsEmptyContentIdFromFileName()
    {
        string path = PlayerFiles.FilePath(_dir, Alice);
        PlayerFiles.Write(path, new PlayerState { SteamId = string.Empty, Username = "Alice" });

        PlayerArchive archive = new();
        archive.Load(_dir);

        Assert.Equal(Alice, archive.TryGet(Alice)!.SteamId);
        Assert.Equal("Alice", archive.TryGet(Alice)!.Username);
    }

    [Fact]
    public void GetOrCreate_UsesFileNameAsIdentity()
    {
        PlayerArchive archive = new();
        PlayerState state = archive.GetOrCreate(ulong.Parse(Alice), "Alice");

        Assert.Equal(Alice, state.SteamId);
        Assert.Equal("Alice", state.Username);
    }

    [Fact]
    public void UpdateUsername_IsPersisted()
    {
        PlayerArchive archive = new();
        PlayerState state = archive.GetOrCreate(ulong.Parse(Alice), "Old Name");
        Assert.True(state.Dirty);
        Assert.True(archive.SaveIfDirty(_dir, Alice));

        Assert.True(PlayerArchive.UpdateUsername(state, "New Name"));
        Assert.True(state.Dirty);
        Assert.True(archive.SaveIfDirty(_dir, Alice));

        PlayerArchive reloaded = new();
        reloaded.Load(_dir);
        Assert.Equal("New Name", reloaded.TryGet(Alice)!.Username);
    }

    [Fact]
    public void Load_RepairsAndMarksCorruptRatingListsDirty()
    {
        string path = PlayerFiles.FilePath(_dir, Alice);
        PlayerFiles.Write(path, new PlayerState
        {
            SteamId = Alice,
            Username = "Alice",
            Likes = ["de_dust2", "de_dust2", ""],
            Dislikes = ["de_dust2", "de_mirage"],
        });

        PlayerArchive archive = new();
        archive.Load(_dir);

        PlayerState state = archive.TryGet(Alice)!;
        Assert.Equal(["de_dust2"], state.Likes);
        Assert.Equal(["de_mirage"], state.Dislikes);
        Assert.True(state.Dirty);
    }
}
