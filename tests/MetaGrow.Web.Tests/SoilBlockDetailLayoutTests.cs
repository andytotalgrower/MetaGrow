using System.Text.Json;
using MetaGrow.Shared.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class SoilBlockDetailLayoutTests
{
    [Theory]
    [InlineData(5, 25, "")]
    [InlineData(10, 75, "")]
    [InlineData(0, 2, "left")]
    [InlineData(100, 98, "right")]
    public void Marker_uses_each_blocks_range_and_identifies_clipped_extremes(double actual, double position, string overflow)
    {
        var marker = SoilBlockDetailLayout.Marker(actual, new() { Minimum = 5, Maximum = 10 });
        Assert.NotNull(marker);
        Assert.Equal(position, marker.Position);
        Assert.Equal(overflow, marker.Overflow);
    }

    [Fact]
    public void Missing_or_invalid_ranges_never_produce_a_fabricated_marker()
    {
        Assert.Null(SoilBlockDetailLayout.Marker(null, new() { Minimum = 1, Maximum = 5 }));
        Assert.Null(SoilBlockDetailLayout.Marker(2, new() { Target = 3 }));
        Assert.Null(SoilBlockDetailLayout.Marker(2, new() { Minimum = 2, Maximum = 2 }));
        Assert.Null(SoilBlockDetailLayout.Marker(2, new() { Minimum = 3, Maximum = 1 }));
        Assert.Null(SoilBlockDetailLayout.Marker(double.NaN, new() { Minimum = 1, Maximum = 5 }));
    }

    [Fact]
    public void Survey_average_counts_available_samples_equally_including_excluded_recommendations()
    {
        var document = new SoilReportDocument { Blocks = [
            new() { Values = new() { ["Sulphur"] = new() { Value = 0 } } },
            new() { Values = new() { ["sulphur"] = new() { Value = 10 } }, Excluded = true },
            new() { Values = new() { ["Sulphur"] = new() { Value = null } } }] };
        Assert.Equal(5, SoilBlockDetailLayout.SurveyAverages(document)["Sulphur"]);
    }

    [Fact]
    public void Saved_options_without_the_new_flag_keep_their_existing_page_selection()
    {
        var old = JsonSerializer.Deserialize<SoilReportDocument>("""{"Status":"Approved","Options":{"ShowBlockDetails":true}}""")!;
        Assert.False(old.Options.ShowIndividualBlockDetails);
        Assert.True(old.Options.ShowBlockDetails);
        old.Options.ShowIndividualBlockDetails = true;
        Assert.True(JsonSerializer.Deserialize<SoilReportDocument>(JsonSerializer.Serialize(old))!.Options.ShowIndividualBlockDetails);
    }

    [Fact]
    public void Continuations_preserve_long_notes_and_respect_each_blocks_saved_measure_visibility()
    {
        var block = new SoilReportBlock { LabSoilId = 10, Definition = "crop", Values = new() { ["Optional"] = new() { Value = 0 } } };
        var advice = string.Join(" ", Enumerable.Range(1, 500).Select(index => "Advice" + index));
        var document = new SoilReportDocument { Blocks = [block],
            DefinitionSnapshot = [new() { Key = "crop", Sections = [new() { Name = "Dynamic", Items = [new() { Field = "Optional", ShowIfZero = false }, new() { Field = "Present", Label = "Present" }] }] }],
            Notes = [new() { RuleId = "long", Title = "Long advice", Text = advice, LabSoilIds = [10] },
                new() { RuleId = "other", Title = "Other block", Text = "Must not appear", LabSoilIds = [11] }] };
        var pages = SoilBlockDetailLayout.Pages(document, block);
        Assert.True(pages.Count > 1);
        Assert.DoesNotContain(pages.SelectMany(page => page.Summaries), item => item.Key == "Optional");
        Assert.DoesNotContain(pages.SelectMany(page => page.Notes), note => note.Title == "Other block");
        var reconstructed = string.Join(" ", pages.SelectMany(page => page.Notes).Select(note => note.Text));
        Assert.Equal(advice.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), reconstructed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
