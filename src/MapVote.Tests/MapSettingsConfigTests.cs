using System.Text.Json;
using MapVote.Config;

namespace MapVote.Tests;

public class MapSettingsConfigTests
{
    [Fact]
    public void Deserialize_ReadsObjectSettings()
    {
        PluginConfig config = JsonSerializer.Deserialize<PluginConfig>(
            """
            {
              "maps": {
                "map_de_dust2": { "enabled": false, "added": 1763856000 },
                "ws_aim_redline": { "added": 42 }
              }
            }
            """)!;

        Assert.False(config.Maps["map_de_dust2"].Enabled);
        Assert.Equal(1_763_856_000, config.Maps["map_de_dust2"].Added);
        Assert.True(config.Maps["ws_aim_redline"].Enabled);
        Assert.Equal(42, config.Maps["ws_aim_redline"].Added);
    }

    [Fact]
    public void Deserialize_IgnoresLegacyMapsVotable()
    {
        PluginConfig config = JsonSerializer.Deserialize<PluginConfig>(
            """
            { "maps_votable": { "map_de_dust2": false } }
            """)!;

        Assert.Empty(config.Maps);
    }

    [Fact]
    public void Serialize_DropsLegacyAndUnknownKeys()
    {
        PluginConfig config = JsonSerializer.Deserialize<PluginConfig>(
            """
            {
              "maps_votable": { "map_de_dust2": false },
              "maps": {
                "map_de_dust2": { "enabled": true, "added": 7, "future": "x" }
              }
            }
            """)!;

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

        Assert.DoesNotContain("maps_votable", json);
        Assert.DoesNotContain("future", json);
        Assert.Contains("\"maps\"", json);
        Assert.Contains("\"added\": 7", json);
        Assert.Contains("\"enabled\": true", json);
    }

    [Fact]
    public void Normalize_ReplacesNullSettingsAndClampsNegativeTimestamp()
    {
        PluginConfig config = new()
        {
            Maps = new Dictionary<string, MapSettings>(StringComparer.Ordinal)
            {
                ["map_de_dust2"] = null!,
                ["map_de_mirage"] = new MapSettings { Added = -5 },
            },
        };

        config.Normalize();

        Assert.NotNull(config.Maps["map_de_dust2"]);
        Assert.True(config.Maps["map_de_dust2"].Enabled);
        Assert.Equal(0, config.Maps["map_de_mirage"].Added);
    }
}
