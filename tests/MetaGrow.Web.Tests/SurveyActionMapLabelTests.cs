using MetaGrow.Web.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class SurveyActionMapLabelTests
{
    [Fact]
    public void Long_name_wraps_completely_inside_a_concave_polygon_away_from_its_hole()
    {
        List<List<SurveyMapPoint>> rings =
        [
            [new(0, 0), new(260, 0), new(260, 180), new(180, 180), new(180, 80), new(0, 80)],
            [new(190, 100), new(230, 100), new(230, 140), new(190, 140)]
        ];
        const string name = "Ram 1x F do not go lychee";

        var label = Assert.Single(SurveyActionMapLabelLayout.Build([(name, rings)]));

        Assert.False(label.IsCallout);
        Assert.Equal(name, string.Join(" ", label.Lines));
        Assert.InRange(label.FontSize, 10, 11);
        // Sample the complete box, not just its center, to catch labels bridging a concavity or hole.
        for (var row = 0; row <= 10; row++)
        {
            for (var column = 0; column <= 10; column++)
            {
                var x = label.X + label.Width * column / 10;
                var y = label.Y + label.Height * row / 10;
                Assert.True(x < 260 && y < 180 && (y < 80 || x > 180));
                Assert.False(x >= 190 && x <= 230 && y >= 100 && y <= 140);
            }
        }
    }

    [Fact]
    public void Wrapping_fits_a_narrow_polygon_without_changing_the_full_name()
    {
        const string name = "Black Pack IX3 F";
        List<List<SurveyMapPoint>> rings = [[new(100, 100), new(180, 100), new(180, 180), new(100, 180)]];
        var label = Assert.Single(SurveyActionMapLabelLayout.Build([(name, rings)]));

        Assert.False(label.IsCallout);
        Assert.InRange(label.Lines.Count, 2, 3);
        Assert.Equal(name, string.Join(" ", label.Lines));
        Assert.InRange(label.X, 100, 180 - label.Width);
        Assert.InRange(label.Y, 100, 180 - label.Height);
    }

    [Fact]
    public void Tiny_polygons_get_separate_callouts_inside_the_expanded_view_box()
    {
        var polygons = Enumerable.Range(0, 10).Select(index => (
            $"ExtremelyLongSingleWordBlockName{index} with a full instruction",
            new List<List<SurveyMapPoint>> { new() { new(100, 300 + index), new(102, 300 + index), new(102, 302 + index), new(100, 302 + index) } }
        )).ToList();
        var labels = SurveyActionMapLabelLayout.Build(polygons);

        Assert.All(labels, label =>
        {
            Assert.True(label.IsCallout);
            Assert.True(label.X + label.Width < 100 || label.X > 102);
            Assert.InRange(label.Anchor.X, 100, 102);
            Assert.InRange(label.Anchor.Y, 300, 311);
        });
        for (var index = 0; index < labels.Count; index++)
        {
            Assert.Equal(polygons[index].Item1.Replace(" ", ""), string.Concat(labels[index].Lines).Replace(" ", ""));
            if (index > 0) Assert.True(labels[index].Y >= labels[index - 1].Y + labels[index - 1].Height);
        }
        var bounds = SurveyActionMapLabelLayout.ViewBox(labels).Split(' ').Select(double.Parse).ToArray();
        Assert.All(labels, label =>
        {
            Assert.True(label.X >= bounds[0] && label.X + label.Width <= bounds[0] + bounds[2]);
            Assert.True(label.Y >= bounds[1] && label.Y + label.Height <= bounds[1] + bounds[3]);
        });
    }
}
