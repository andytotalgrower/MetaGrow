using ApiModels;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class SurveyActionMapTests
{
    [Fact]
    public void Hectares_match_a_small_spherical_rectangle_in_either_winding_and_open_or_closed_rings()
    {
        // A tiny latitude/longitude rectangle has a known spherical area, independent of SVG projection.
        const double radius = 6_371_009;
        var expected = radius * radius * (0.001 * Math.PI / 180) *
            Math.Abs(Math.Sin(-27 * Math.PI / 180) - Math.Sin(-27.001 * Math.PI / 180)) / 10_000;
        var coordinates = "153,-27 153.001,-27 153.001,-27.001 153,-27.001";
        foreach (var ring in new[] { coordinates, string.Join(" ", coordinates.Split(' ').Reverse()), coordinates + " 153,-27" })
        {
            var data = Fixture();
            data.Blocks[0].Kml = [$"<kml><Polygon><outerBoundaryIs><LinearRing><coordinates>{ring}</coordinates></LinearRing></outerBoundaryIs></Polygon></kml>"];
            var shape = SurveyActionMapLayout.Build(data, "banana").Maps[0].Shapes[0];
            Assert.InRange(shape.AreaHectares, expected - 0.000001, expected + 0.000001);
        }
    }

    [Theory]
    [InlineData("banana", "multicrop")]
    [InlineData("multicrop", "banana")]
    public void Block_areas_combine_polygons_subtract_holes_and_totals_count_blocks_once(string type, string otherType)
    {
        var data = Fixture(type);
        var originalArea = SurveyActionMapLayout.Build(data, type).Maps[0].Shapes[0].AreaHectares;
        var hole = "<innerBoundaryIs><LinearRing><coordinates>153.0002,-27.0002 153.0004,-27.0002 153.0004,-27.0004 153.0002,-27.0004</coordinates></LinearRing></innerBoundaryIs>";
        // Use a separate polygon in the same block to exercise the combined block label.
        var secondPolygon = _polygon.Replace("153.001", "153.021").Replace("153,", "153.02,");
        data.Blocks[0].Kml = [$"<kml><MultiGeometry>{_polygon.Replace("</Polygon>", hole + "</Polygon>")}{secondPolygon}</MultiGeometry></kml>"];
        data.Blocks.Add(new() { BlockId = 1, BlockType = otherType, BlockName = "Other crop", Kml = [$"<kml>{_polygon}</kml>"] });
        data.Recommendations[0].BlockIds = [1, 1, 2];
        data.Recommendations.Add(new() { SurveyActionId = 11, ActionGroupId = 1, ActionGroupName = "Nutrition", Summary = "Second job", BlockIds = [1, 2] });

        var map = Assert.Single(SurveyActionMapLayout.Build(data, type).Maps);
        var blockShapes = map.Shapes.Where(shape => shape.BlockName == "Block A").ToList();
        Assert.Equal(2, blockShapes.Count);
        Assert.Equal(blockShapes[0].AreaHectares, blockShapes[1].AreaHectares);
        Assert.InRange(blockShapes[0].AreaHectares, originalArea * 1.95999, originalArea * 1.96001);
        var legend = Assert.Single(map.Legend);
        Assert.Equal(new[] { "Block A (2.16 ha)", "Block B (1.10 ha)" }, legend.BlockDescriptions);
        Assert.Equal(blockShapes[0].AreaHectares + map.Shapes.Single(shape => shape.BlockName == "Block B").AreaHectares,
            legend.AreaHectares);
        Assert.False(legend.HasIncompleteArea);
    }

    [Fact]
    public void Missing_or_partially_invalid_boundaries_mark_recommendation_areas_incomplete()
    {
        var data = Fixture();
        data.Blocks[1].Kml.Clear();
        var map = Assert.Single(SurveyActionMapLayout.Build(data, "banana").Maps);
        Assert.True(map.Legend[0].HasIncompleteArea);
        Assert.Equal(map.Shapes[0].AreaHectares, map.Legend[0].AreaHectares);

        data.Blocks[1].Kml = [$"<kml>{_polygon}</kml>", "<invalid"];
        map = Assert.Single(SurveyActionMapLayout.Build(data, "banana").Maps);
        Assert.True(map.Legend[0].HasIncompleteArea);
        Assert.Equal(map.Shapes.Sum(shape => shape.AreaHectares), map.Legend[0].AreaHectares);
    }

    [Fact]
    public void A_partial_storage_read_only_marks_recommendations_for_affected_blocks()
    {
        var data = Fixture();
        data.Recommendations[0].BlockIds = [1];
        data.Recommendations.Add(new() { SurveyActionId = 20, ActionGroupId = 2, ActionGroupName = "Disease",
            Summary = "Trim", BlockIds = [2] });
        data.Warnings = ["Map data could not be loaded for Block A.", "Other map diagnostic"];

        var maps = SurveyActionMapLayout.Build(data, "banana").Maps;
        Assert.True(maps.Single(map => map.Key == "1:").Legend[0].HasIncompleteArea);
        Assert.False(maps.Single(map => map.Key == "2:").Legend[0].HasIncompleteArea);
    }

    [Fact]
    public void Work_summary_groups_recommendations_and_keeps_blocks_without_geometry()
    {
        var data = Fixture();
        data.Recommendations[0].Summary = "21/10 Setbest recommended";
        data.Recommendations[0].BlockIds = [1];
        data.Recommendations.Add(new() { SurveyActionId = 12, ActionGroupId = 1, ActionGroupName = "Nutrition",
            Summary = "21/10 Setbest recommended", BlockIds = [2, 2, 99] });
        data.Recommendations.Add(new() { SurveyActionId = 13, ActionGroupId = 1, ActionGroupName = "Nutrition",
            Summary = "22/10 Setbest recommended", BlockIds = [1] });
        data.Blocks[1].Kml.Clear();
        data.Blocks.Add(new() { BlockId = 1, BlockType = "multicrop", BlockName = "Other crop" });
        var view = SurveyActionMapLayout.Build(data, "banana");
        var work = Assert.Single(view.WorkSummary, item => item.Recommendation == "21/10 Setbest recommended");
        Assert.Equal(new[] { "Block A", "Block B" }, work.Blocks);
        Assert.Equal(2, view.WorkSummary.Count); // Dates/rates in the instruction must not be merged away.
        Assert.Contains("Block B", view.MissingKmlBlocks);
        Assert.DoesNotContain("Block 99", view.MissingKmlBlocks);
        Assert.All(view.Maps.SelectMany(map => map.Legend), item => Assert.DoesNotContain("Block 99", item.Blocks));
        Assert.DoesNotContain(view.Warnings, warning => warning.StartsWith("No usable KML"));
    }

    [Fact]
    public void Work_summary_is_available_when_no_polygons_exist()
    {
        var data = Fixture();
        data.Blocks.ForEach(block => block.Kml.Clear());
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Empty(view.Maps);
        Assert.Equal(new[] { "Block A", "Block B" }, Assert.Single(view.WorkSummary).Blocks);
        Assert.Equal(new[] { "Block A", "Block B" }, view.MissingKmlBlocks);
    }

    // Small real-coordinate polygons exercise projection, holes, and multi-polygon KML without storage access.
    private const string _polygon = "<Polygon><outerBoundaryIs><LinearRing><coordinates>153,-27 153.001,-27 153.001,-27.001 153,-27.001 153,-27</coordinates></LinearRing></outerBoundaryIs></Polygon>";

    public static SurveyActionMapDto Fixture(string type = "banana") => new()
    {
        Recommendations = [new() { SurveyActionId = 10, ActionGroupId = 1, ActionGroupName = "Nutrition", Summary = "21/10 Apply fertiliser", BlockIds = [1, 2], OriginalItemNames = new() { [11] = "" } }],
        Blocks = [new() { BlockId = 1, BlockType = type, BlockName = "Block A", Kml = [$"<kml xmlns='http://www.opengis.net/kml/2.2'><Document>{_polygon}</Document></kml>"] },
                  new() { BlockId = 2, BlockType = type, BlockName = "Block B", Kml = [$"<kml>{_polygon.Replace("153.001", "153.011").Replace("153,", "153.01,")}</kml>"] }]
    };

    [Fact]
    public void No_actions_means_no_maps_or_kml_parsing()
    {
        var data = Fixture();
        data.Recommendations.Clear();
        data.Blocks[0].Kml = ["invalid XML"];
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Empty(view.Maps);
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void Missing_and_invalid_kml_keep_recommendations_and_valid_maps()
    {
        var data = Fixture();
        data.Blocks[1].Kml = ["<not-valid"];
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Single(view.Maps);
        Assert.Single(view.Maps[0].Shapes);
        Assert.Contains(view.Warnings, warning => warning.Contains("Block B"));
        data.Blocks[0].Kml.Clear();
        view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Empty(view.Maps);
        Assert.Single(data.Recommendations);
        Assert.Contains(view.Warnings, warning => warning.Contains("No block boundaries"));
    }

    [Fact]
    public void Names_are_scoped_to_category_and_block_type_even_with_colliding_ids()
    {
        var data = Fixture();
        data.Recommendations[0].MapName = "O'Brien";
        data.Recommendations.Add(new() { SurveyActionId = 12, ActionGroupId = 2, ActionGroupName = "Disease", MapName = "O'Brien", Summary = "Disease only", BlockIds = [1] });
        data.Blocks.Add(new() { BlockId = 1, BlockType = "multicrop", BlockName = "Other crop", Kml = [$"<kml>{_polygon}</kml>"] });
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Equal(2, view.Maps.Count);
        Assert.DoesNotContain(view.Maps[0].Legend, item => item.Summary.Contains("Disease"));
        Assert.All(view.Maps, map => Assert.Equal("", map.Shapes.Single(shape => shape.BlockName == "Other crop").Summary));
        Assert.All(view.Maps, map => Assert.True(map.Visible));
    }

    [Fact]
    public void Combined_summaries_have_stable_colours_and_unnamed_map_contains_category()
    {
        var data = Fixture();
        data.Recommendations.Add(new() { SurveyActionId = 12, ActionGroupId = 1, ActionGroupName = "Nutrition", MapName = "Second", Summary = "Second action", BlockIds = [1, 2] });
        var view = SurveyActionMapLayout.Build(data, "banana");
        var all = view.Maps.Single(map => map.Key == "1:");
        Assert.Single(all.Legend);
        Assert.Contains("Apply fertiliser + Second action", all.Legend[0].Summary);
        Assert.Equal(all.Shapes[0].Colour, all.Shapes[1].Colour);
        data.Recommendations.Reverse();
        var reordered = SurveyActionMapLayout.Build(data, "banana").Maps.Single(map => map.Key == "1:");
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(all.Legend), System.Text.Json.JsonSerializer.Serialize(reordered.Legend));
        Assert.Equal(new[] { "Block A", "Block B" }, all.Legend[0].Blocks);
    }

    [Fact]
    public void Multi_polygon_and_inner_rings_are_preserved_and_entities_rejected()
    {
        var data = Fixture();
        var hole = "<innerBoundaryIs><LinearRing><coordinates>153.0002,-27.0002 153.0003,-27.0002 153.0003,-27.0003 153.0002,-27.0002</coordinates></LinearRing></innerBoundaryIs>";
        data.Blocks[0].Kml = [$"<kml><MultiGeometry>{_polygon.Replace("</Polygon>", hole + "</Polygon>")}{_polygon}</MultiGeometry></kml>"];
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Equal(3, view.Maps[0].Shapes.Count);
        Assert.Equal(2, view.Maps[0].Shapes[0].Path.Count(character => character == 'M'));
        data.Blocks[0].Kml = ["<!DOCTYPE kml [<!ENTITY x SYSTEM 'file:///secret'>]><kml>&x;</kml>"];
        view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Single(view.Maps[0].Shapes);
        Assert.Contains(view.Warnings, warning => warning.Contains("Invalid map data"));
    }

    [Theory]
    [InlineData("NaN,-27")]
    [InlineData("153,Infinity")]
    [InlineData("181,-27")]
    public void Invalid_coordinates_do_not_produce_broken_svg(string coordinate)
    {
        var data = Fixture();
        data.Blocks[0].Kml = [$"<kml>{_polygon.Replace("153,-27", coordinate)}</kml>"];
        var view = SurveyActionMapLayout.Build(data, "banana");
        Assert.Single(view.Maps[0].Shapes);
        Assert.DoesNotContain("NaN", view.Maps[0].Shapes[0].Path);
    }
}
