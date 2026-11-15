using System.Text.Json;
using MapVote.Players;

namespace MapVote.Tests;

public sealed class MapPlayCountsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapvote-plays-").FullName;

    private string FilePath => Path.Combine(_dir, "map_plays.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("workshop_preview_1")]
    [InlineData("not a map")]
    public void Increment_IgnoresUnplayableNames(string map)
    {
        MapPlayCounts plays = new();

        plays.Increment(map);

        Assert.Equal(0, plays.CountOf(map));
        plays.SaveFile(FilePath);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Increment_NormalizesCaseAndCountOfUsesSameKey()
    {
        MapPlayCounts plays = new();

        plays.Increment("DE_DUST2");
        plays.Increment("de_dust2");

        Assert.Equal(2, plays.CountOf("de_dust2"));
        Assert.Equal(2, plays.CountOf("DE_DUST2"));
    }

    [Fact]
    public void LoadFile_DropsInvalidAndNegativeEntriesAndKeepsHighestDuplicate()
    {
        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["de_dust2"] = 3,
                    ["DE_DUST2"] = 5,
                    ["../escape"] = 9,
                    ["de_mirage"] = -1,
                },
                PlayerFiles.JsonOptions));

        MapPlayCounts plays = new();
        plays.LoadFile(FilePath);

        Assert.Equal(5, plays.CountOf("de_dust2"));
        Assert.Equal(0, plays.CountOf("de_mirage"));
        Assert.Equal(0, plays.CountOf("../escape"));
    }

    [Fact]
    public void SaveFile_RoundTripsIncrements()
    {
        MapPlayCounts plays = new();
        plays.Increment("de_nuke");
        plays.SaveFile(FilePath);

        MapPlayCounts reloaded = new();
        reloaded.LoadFile(FilePath);

        Assert.Equal(1, reloaded.CountOf("de_nuke"));
    }

    [Fact]
    public void Increment_DoesNotOverflowAtIntMax()
    {
        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["de_dust2"] = int.MaxValue,
                },
                PlayerFiles.JsonOptions));

        MapPlayCounts plays = new();
        plays.LoadFile(FilePath);
        plays.Increment("de_dust2");

        Assert.Equal(int.MaxValue, plays.CountOf("de_dust2"));
    }
}
