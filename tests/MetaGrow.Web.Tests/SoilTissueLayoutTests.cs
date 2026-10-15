using MetaGrow.Shared.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class SoilTissueLayoutTests
{
    [Fact]
    public void Tissue_overview_omits_uncollected_blocks_without_removing_soil_rows_or_changing_counts()
    {
        var report = Example(30, 2);
        report.Tissue.Samples = report.Tissue.Samples.Where(sample => sample.LabTissueId is 2 or 28).ToList();
        var matrices = SoilStatusOverviewLayout.Pages(report).SelectMany(page => page).ToList();
        Assert.Equal(30, matrices.Where(matrix => !matrix.IsTissue).Sum(matrix => matrix.Rows.Count));
        var tissue = Assert.Single(matrices, matrix => matrix.IsTissue);
        Assert.Equal(2, tissue.TotalRows);
        Assert.Equal(new[] { 2, 28 }, tissue.Rows.Select(row => row.Tissue!.LabTissueId));
        Assert.DoesNotContain(tissue.Rows, row => row.Tissue is null);
        Assert.Equal(30, report.Blocks.Count);
    }

    [Fact]
    public void Small_overviews_share_a_page_but_large_surveys_continue_without_losing_samples()
    {
        var report = Example(6, 12);
        var combined = Assert.Single(SoilStatusOverviewLayout.Pages(report));
        Assert.Equal(new[] { false, true }, combined.Select(matrix => matrix.IsTissue));
        report = Example(28, 21);
        var pages = SoilStatusOverviewLayout.Pages(report);
        Assert.All(pages, page => Assert.True(page.Sum(matrix => matrix.EstimatedHeight) <= 225));
        var tissue = pages.SelectMany(page => page).Where(matrix => matrix.IsTissue).ToList();
        Assert.Equal(28 * 21, tissue.Sum(matrix => matrix.Rows.Count * matrix.Measures.Count));
        Assert.All(tissue, matrix => Assert.True(32 + matrix.Measures.Sum(measure => matrix.Widths[measure.Key]) * 7.5 <= 186));
    }

    [Fact]
    public void Repeated_tissue_samples_and_parts_retain_separate_values_after_all_soil_rows()
    {
        var report = Example(6, 30);
        report.Tissue.Samples.Add(new() { LabTissueId = 101, SoilLabIds = [1], BlockName = "Block 1", SamplePart = "Leaf", SampleReference = "Repeat",
            Values = new() { ["M0"] = new() { Value = 999, Target = 4, Minimum = 3, Maximum = 5 } } });
        report.Tissue.Samples.Add(new() { LabTissueId = 102, SoilLabIds = [1], BlockName = "Block 1", SamplePart = "Petiole", SampleReference = "Petiole",
            Values = new() { ["M0"] = new() { Value = 777 } } });
        var pages = SoilReportTableLayout.Pages(report);
        foreach (var blockPages in pages.GroupBy(page => page.Blocks[0].LabSoilId))
        {
            var groups = blockPages.SelectMany(page => page.Groups).ToList();
            Assert.Equal(30, groups.Where(group => !group.IsTissue).Sum(group => group.Measures.Count));
            Assert.DoesNotContain(groups.SkipWhile(group => !group.IsTissue), group => !group.IsTissue);
            Assert.All(blockPages, page => Assert.True(page.Groups.Sum(group => group.Measures.Count + (group.IsTissue ? 3 : 0)) <= 24));
        }
        var firstBlockGroups = pages.Where(page => page.Blocks[0].LabSoilId == 1).SelectMany(page => page.Groups).Where(group => group.IsTissue).ToList();
        Assert.Equal(32, firstBlockGroups.Sum(group => group.Measures.Count));
        Assert.Contains(firstBlockGroups, group => group.Value(report.Blocks[0], 0, "M0")?.Value == 999);
        Assert.Contains(firstBlockGroups, group => group.Value(report.Blocks[0], 0, "M0")?.Value == 777);
        // Soil and tissue can use the same key without one overwriting the other.
        Assert.Equal(2, report.Blocks[0].Values["M0"].Value);
        Assert.Equal(7, SoilStatusOverviewLayout.Pages(report).SelectMany(page => page).First(matrix => matrix.IsTissue).Rows.Count(row => row.Tissue?.SamplePart == "Leaf"));
    }

    [Fact]
    public void Zero_only_tissue_rows_are_omitted_without_mutating_saved_values_or_hiding_mixed_zeroes()
    {
        var report = Example(2, 3);
        report.Tissue.Samples[0].Values["M0"].Value = 0;
        report.Tissue.Samples[1].Values["M0"].Value = null;
        report.Tissue.Samples[0].Values["M1"].Value = 0;
        report.Tissue.Samples[1].Values["M1"].Value = 5;
        var before = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Equal(new[] { "M1", "M2" }, TissueReportLayout.VisibleMeasures(report, report.Tissue.Samples).Select(measure => measure.Key));
        var table = Assert.Single(SoilReportTableLayout.Pages(report));
        var tissue = Assert.Single(table.Groups, group => group.IsTissue);
        Assert.Equal(0, tissue.Value(report.Blocks[0], 0, "M1")!.Value);
        Assert.DoesNotContain(tissue.Measures, measure => measure.Key == "M0");
        Assert.DoesNotContain(SoilBlockDetailLayout.Pages(report, report.Blocks[0]).SelectMany(page => page.Tissue), row => row.Measure.Key is "M0" or "M1");
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(report));
    }

    [Fact]
    public void Tissue_overview_uses_included_soil_blocks_once_for_repeated_depths_and_rejects_orphans()
    {
        var report = Example(2, 1);
        report.Blocks.Add(new() { LabSoilId = 3, BlockId = 1, Name = "Block 1", Crop = "Banana" });
        report.Tissue.Samples[0].SoilLabIds.Add(3);
        report.Blocks[1].Excluded = true;
        report.Tissue.Samples.Add(new() { LabTissueId = 99, SoilLabIds = [99], BlockName = "Orphan" });
        var tissue = Assert.Single(SoilStatusOverviewLayout.Pages(report).SelectMany(page => page), matrix => matrix.IsTissue);
        Assert.Single(tissue.Rows);
        Assert.Equal(1, tissue.Rows[0].Tissue!.LabTissueId);
    }

    private static SoilReportDocument Example(int blocks, int measures)
    {
        var report = new SoilReportDocument();
        for (var index = 0; index < measures; index++)
        {
            report.Measures.Add(new() { Key = "M" + index, Label = "Measure " + index, Section = "Soil", NumberFormat = "0.0" });
            report.Tissue.Measures.Add(new() { Key = "M" + index, Label = "Measure " + index, Section = "Tissue", NumberFormat = "0.0", ShowIfZero = true });
        }
        for (var index = 1; index <= blocks; index++)
        {
            var block = new SoilReportBlock { LabSoilId = index, BlockId = index, Name = "Block " + index, Crop = "Banana" };
            var sample = new TissueReportSample { LabTissueId = index, SoilLabIds = [index], BlockName = block.Name, SamplePart = "Leaf" };
            foreach (var measure in report.Measures)
            {
                block.Values[measure.Key] = new() { Value = 2, Target = 4, Minimum = 3, Maximum = 5 };
                sample.Values[measure.Key] = new() { Value = 4, Target = 4, Minimum = 3, Maximum = 5 };
            }
            report.Blocks.Add(block); report.Tissue.Samples.Add(sample);
        }
        return report;
    }
}
