using MapVote.Maps;

namespace MapVote.Tests;

public class MapGroupListTests
{
    [Fact]
    public void Parse_WorkshopCollection_TreatsBareNamesAsWorkshop()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: workshop
                de_assembly
                ar_shoots
            """);

        Assert.True(result.Ready);
        Assert.Empty(result.Local);
        Assert.Equal(
            [new MapEntry("de_assembly", MapOrigin.Workshop), new MapEntry("ar_shoots", MapOrigin.Workshop)],
            result.Workshop);
    }

    [Fact]
    public void Parse_AdminMapgroup_KeepsBareLocalAndPathWorkshop()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: mg_custom
                cs_office
                de_dust2
                workshop/3071005299/de_assembly
                workshop/3070552133/ar_shoots
            """);

        Assert.True(result.Ready);
        Assert.Equal(
            [new MapEntry("cs_office", MapOrigin.Local), new MapEntry("de_dust2", MapOrigin.Local)],
            result.Local);
        Assert.Equal(
            [new MapEntry("de_assembly", MapOrigin.Workshop), new MapEntry("ar_shoots", MapOrigin.Workshop)],
            result.Workshop);
        Assert.Equal("de_assembly", result.WorkshopNamesById["3071005299"]);
        Assert.Equal("ar_shoots", result.WorkshopNamesById["3070552133"]);
    }

    [Fact]
    public void Parse_MixedMapgroup_SkipsHelperAndDeniedLines()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: mg_mixed
                cs_office
                workshop/3071005299/de_assembly
                de_dust2_vanity
                de_nuke_skybox
                prefabs/de_dust2/de_dust2_skybox
                workshop_preview_ancient
                invalid name
            """);

        Assert.Equal(2, result.Local.Count + result.Workshop.Count);
        Assert.Equal(5, result.Skipped);
    }

    [Fact]
    public void Parse_HeaderOnly_IsNotReady()
    {
        MapGroupParseResult result = MapGroupList.Parse("Map group: mg_custom");

        Assert.False(result.Ready);
        Assert.Empty(result.Local);
        Assert.Empty(result.Workshop);
    }

    [Fact]
    public void Parse_ExplicitEmptyList_IsReady()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: mg_custom
            No maps in mapgroup map list
            """);

        Assert.True(result.Ready);
        Assert.Empty(result.Local);
        Assert.Empty(result.Workshop);
    }

    [Fact]
    public void Parse_DuplicateNames_AreSkippedWithinTheirOrigin()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: mg_custom
                de_dust2
                de_dust2
                workshop/1/de_assembly
                workshop/2/de_assembly
            """);

        Assert.Single(result.Local);
        Assert.Single(result.Workshop);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public void Parse_WorkshopGroupName_IsCaseInsensitiveAndTrimmed()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group:   WORKSHOP
                de_assembly
            """);

        Assert.Single(result.Workshop);
        Assert.Empty(result.Local);
    }

    [Fact]
    public void Parse_RepeatedHeaders_ChangeOriginForFollowingLines()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            Map group: workshop
                de_assembly
            Map group: mg_active
                de_dust2
            """);

        Assert.Equal([new MapEntry("de_dust2", MapOrigin.Local)], result.Local);
        Assert.Equal([new MapEntry("de_assembly", MapOrigin.Workshop)], result.Workshop);
    }

    [Fact]
    public void Parse_IgnoresCommandEcho()
    {
        MapGroupParseResult result = MapGroupList.Parse(
            """
            print_mapgroup_sv
            Map group: workshop
                de_assembly
            """);

        Assert.Equal([new MapEntry("de_assembly", MapOrigin.Workshop)], result.Workshop);
        Assert.Equal(0, result.Skipped);
    }
}
