using System.Text.Json;
using MapVote.Config;

namespace MapVote.Tests;

public class CatalogConfigTests
{
    [Fact]
    public void Defaults_EnableBothSources()
    {
        CatalogConfig config = new();

        Assert.True(config.QueryLocalMaps);
        Assert.True(config.QueryMapGroup);
    }

    [Fact]
    public void Json_UsesDocumentedKeys()
    {
        CatalogConfig config = JsonSerializer.Deserialize<CatalogConfig>(
            """
            {
              "query_local_maps": false,
              "query_mapgroup": true
            }
            """)!;

        Assert.False(config.QueryLocalMaps);
        Assert.True(config.QueryMapGroup);
    }
}
