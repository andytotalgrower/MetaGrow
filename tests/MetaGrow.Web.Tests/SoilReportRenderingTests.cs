using System.Text.RegularExpressions;
using System.Text.Json;
using DevExpress.Blazor;
using MetaGrow.Shared.Models;
using MetaGrow.Web.Components.Shared;
using MetaGrow.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace MetaGrow.Web.Tests;

public sealed class SoilReportRenderingTests
{
    [Theory]
    [InlineData(true, true, "Soil And Tissue Report", "Soil_Tissue")]
    [InlineData(true, false, "Soil Nutrient Balance Report", "Soil")]
    [InlineData(false, true, "Tissue Report", "Tissue")]
    public async Task Material_choices_control_the_complete_saved_report_without_discarding_hidden_data(bool soil, bool tissue, string title, string fileLabel)
    {
        var report = Example(); report.Blocks = report.Blocks.Take(2).ToList();
        report.Options.IncludeSoil = soil; report.Options.IncludeTissue = tissue;
        report.Options.ShowIndividualBlockDetails = true;
        report.Tissue.Measures = [new() { Key = "NitrogenPc", Label = "Tissue nitrogen %", NumberFormat = "0.00", Section = "Tissue Nutrients" }];
        report.Tissue.Samples = [new() { LabTissueId = 200, SoilLabIds = [1], BlockName = "Block 1", SampleReference = "TISSUE-200",
            TemplateName = "Foliar targets", SamplePart = "Leaf", Values = new() { ["NitrogenPc"] = new() { Value = 3.2, Target = 3, Minimum = 2.5, Maximum = 4 } } }];
        report.Notes = [new() { RuleId = "disclaimer", Title = "Disclaimer", Text = "Shared report disclaimer", LabSoilIds = [1, 2] },
            new() { RuleId = "crop-sugar", Title = "Soil-only guidance", Text = "Soil-only application advice", LabSoilIds = [1, 2] }];
        report.History.Add(new() { Date = report.SurveyDate, SurveyId = 100, Averages = report.History[0].Averages });
        report.Blocks[0].SugarInputs.Add(new() { Nutrient = "Nitrogen", Stage = "Plant", Amount = 60 });
        report.Warnings = ["Block 1: Soil-only calculation warning", "Tissue results are pending for another sample."];
        var before = JsonSerializer.Serialize(report);
        var html = await Render(report);
        Assert.Equal(title, report.Title); Assert.Equal(fileLabel, report.FileMaterialLabel);
        foreach (var phrase in new[] { "soil samples reported", "Dynamic measure 1", "Theoretical Elemental Requirements", "Soil Balance Recommendations",
            "Historical Farm Averages", "Sugarcane Allowable Inputs", "Soil-only application advice", "Soil-only calculation warning", "Soil cropping", "12341" })
            Assert.Equal(soil, html.Contains(phrase, StringComparison.Ordinal));
        foreach (var phrase in new[] { "tissue sample reported", "Tissue nitrogen %", "TISSUE-200", "Foliar targets", "class=\"detail-tissue\"", "Tissue results are pending" })
            Assert.Equal(tissue, html.Contains(phrase, StringComparison.Ordinal));
        Assert.Equal(soil, html.Contains("Block 2", StringComparison.Ordinal)); // No tissue was collected there.
        Assert.Contains("Shared report disclaimer", html); Assert.Contains("Tailored action heading", html);
        Assert.Contains("Against target range", html);
        Assert.Equal(before, JsonSerializer.Serialize(report));
        // The shared route and printing both use this same frozen document renderer.
        await ExportPreview(html, $"material-{fileLabel.ToLowerInvariant()}.html");
    }

    [Fact]
    public void Older_revisions_default_to_combined_when_tissue_exists_and_soil_when_it_does_not()
    {
        var report = JsonSerializer.Deserialize<SoilReportDocument>("{\"Options\":{}}")!;
        Assert.True(report.Options.IncludeSoil); Assert.True(report.Options.IncludeTissue);
        Assert.Equal("Soil Nutrient Balance Report", report.Title);
        report.Blocks.Add(new() { LabSoilId = 1 }); report.Tissue.Samples.Add(new() { SoilLabIds = [1] });
        Assert.Equal("Soil And Tissue Report", report.Title);
    }

    [Fact]
    public async Task Combined_report_appends_tissue_to_soil_tables_and_aligns_block_detail_ranges()
    {
        var report = Example(); report.Blocks = report.Blocks.Take(6).ToList();
        report.Options.ShowIndividualBlockDetails = true;
        var names = new[] { "3C + 4 C", "5A", "7D", "8 B", "9E", "Mango" };
        var nitrogen = new[] { 3.55, 3.78, 3.62, 3.55, 3.36 };
        report.Tissue.Measures = [new() { Key = "NitrogenPc", Label = "Nitrogen %", NumberFormat = "#,##0.00", Section = "Tissue Nutrients" },
            new() { Key = "BoronPpm", Label = "Unreported boron ppm", NumberFormat = "#,##0.0", Section = "Tissue Nutrients", ShowIfZero = true },
            new() { Key = "SiliconPpm", Label = "Silicon ppm", NumberFormat = "#,##0.0", Section = "Tissue Nutrients" }];
        for (var index = 0; index < 6; index++)
        {
            report.Blocks[index].Name = names[index]; report.Blocks[index].Crop = "Banana";
            if (index == 5) continue;
            report.Tissue.Samples.Add(new() { LabTissueId = 200 + index, SoilLabIds = [report.Blocks[index].LabSoilId], BlockName = names[index],
                Crop = "Banana", TemplateId = 20, TemplateName = "Banana tissue targets", SamplePart = "Leaf", SampleReference = "T-" + index,
                Values = new() { ["NitrogenPc"] = new() { Value = nitrogen[index], Minimum = 3.4, Target = 3.6, Maximum = 3.7 },
                    ["BoronPpm"] = new() { Value = index == 0 ? null : 0 },
                    ["SiliconPpm"] = new() { Value = index == 0 ? 0 : 10 } } });
        }
        // An orphaned or excluded tissue source cannot leak into tables or cover counts.
        report.Tissue.Samples.Add(new() { LabTissueId = 999, SoilLabIds = [999], BlockName = "Tissue-only block", Crop = "Banana" });
        var html = await Render(report);
        var tables = Regex.Matches(html, "<table class=\"results\".*?</table>", RegexOptions.Singleline)
            .Select(match => match.Value).Where(table => table.Contains("Tissue Nutrients")).ToList();
        Assert.Equal(2, tables.Count); // Existing four-block soil groups, with tissue appended to each.
        Assert.Contains("Dynamic measure 25", tables[0]);
        Assert.True(tables[0].IndexOf("Dynamic measure 25", StringComparison.Ordinal) < tables[0].IndexOf("Tissue Nutrients", StringComparison.Ordinal));
        Assert.Contains("Mango", tables[1]); Assert.Contains("No tissue result", tables[1]);
        Assert.DoesNotContain("tissue-sheet", html);
        Assert.Contains("5 tissue samples reported", html); Assert.DoesNotContain("Tissue-only block", html);
        Assert.Contains("Soil And Tissue", html);
        Assert.Contains("status-low", tables[1]); Assert.Contains("status-high", tables[0]); Assert.Contains("status-range", tables[0]);
        Assert.Contains("status-unrated", tables[0]); Assert.Contains("status-missing", tables[1]);
        Assert.DoesNotContain("Unreported boron ppm", html); // All zero/null, even with ShowIfZero configured.
        Assert.Contains("Silicon ppm 0.0", tables[0]); // A mixed row keeps its measured zero.
        Assert.Equal(5, Regex.Matches(html, "class=\"detail-tissue\"").Count);
        var details = Regex.Matches(html, "<table class=\"detail-results detail-tissue-results\".*?</table>", RegexOptions.Singleline);
        Assert.Equal(5, details.Count);
        Assert.All(details.Cast<Match>(), table => Assert.Contains("<th>Result</th><th>Range</th><th>Against target range</th>", Regex.Replace(table.Value, " b-[a-z0-9]+", "")));
        Assert.All(details.Cast<Match>(), table => Assert.Contains("class=\"detail-track\"", table.Value));
        var overviewPages = Regex.Matches(html, "<section class=\"soil-sheet overview-sheet\".*?</section>", RegexOptions.Singleline);
        Assert.Equal(2, overviewPages.Count);
        Assert.Equal(2, Matrices(overviewPages[1].Value).Count); // Last soil matrix shares its page with tissue.
        Assert.DoesNotContain("Leaf · T-", overviewPages[1].Value);
        Assert.DoesNotMatch("<small[^>]*>Leaf", tables[0]);
        Assert.DoesNotContain("Silicon ppm", Matrices(overviewPages[1].Value)[1]); // No configured target.
        await ExportPreview(html, "soil-tissue-report.html");
        report.Blocks[0].Excluded = true;
        var excluded = await Render(report);
        Assert.Contains("4 tissue samples reported", excluded);
        Assert.DoesNotContain("T-0", excluded);
    }

    [Fact]
    public async Task Tissue_ranges_use_template_IDs_even_when_two_templates_have_the_same_name()
    {
        var report = Example(); report.Blocks = [report.Blocks[0], report.Blocks[5]];
        report.Tissue.Measures = [new() { Key = "NitrogenPc", Label = "Nitrogen %", NumberFormat = "#,##0.0" }];
        foreach (var block in report.Blocks)
            report.Tissue.Samples.Add(new() { LabTissueId = block.LabSoilId, SoilLabIds = [block.LabSoilId], BlockName = block.Name,
                Crop = block.Crop, TemplateId = block.LabSoilId, TemplateName = "Same display name",
                Values = new() { ["NitrogenPc"] = new() { Value = 2, Minimum = 1, Target = block.LabSoilId, Maximum = 10 } } });
        var table = Assert.Single(Regex.Matches(await Render(report), "<table class=\"results\".*?</table>", RegexOptions.Singleline)
            .Select(match => match.Value), value => value.Contains("Tissue Nutrients"));
        Assert.Contains("Varies by block", table);
        Assert.Contains("Target 1.0; range 1.0 to 10.0", table);
        Assert.Contains("Target 6.0; range 1.0 to 10.0", table);

    }

    [Fact]
    public async Task Cover_describes_the_saved_report_and_does_not_hide_QA_status_or_excluded_samples()
    {
        var report = Example();
        report.Status = "Ready for QA";
        report.PropertyName = "Northern Valley Orchards — Davidson Road & River Flats";
        report.AgronomistEmail = "agronomist@example.test";
        report.AgronomistPhone = "0400 000 000";
        report.Blocks[0].Excluded = true;
        report.Blocks[0].Crop = "Excluded crop";
        var html = await Render(report);
        var cover = Assert.Single(Regex.Matches(html, "<section class=\"soil-sheet cover-sheet\".*?</section>", RegexOptions.Singleline)).Value;
        Assert.Contains("8 soil samples reported", cover);
        Assert.DoesNotContain("Excluded crop", cover);
        Assert.Contains("Avocado", cover); Assert.Contains("Banana", cover);
        Assert.Contains("agronomist@example.test", cover); Assert.Contains("0400 000 000", cover);
        Assert.True(cover.IndexOf("agronomist@example.test", StringComparison.Ordinal) < cover.IndexOf("0400 000 000", StringComparison.Ordinal));
        Assert.Contains("Ready for QA — this report has not completed QA.", cover);
        Assert.Contains("15 September 2026", cover);
        // Variable actions and warnings remain after the separate cover, with all saved advice intact.
        Assert.DoesNotContain("Tailored action heading", cover);
        Assert.Contains("Tailored action heading", html);
        await ExportPreview(html, "soil-report-cover-review.html");
        report.Status = "Approved";
        report.Blocks = report.Blocks.Where(block => !block.Excluded).Take(1).ToList();
        cover = Regex.Match(await Render(report), "<section class=\"soil-sheet cover-sheet\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Contains("1 soil sample reported", cover);
        Assert.DoesNotContain("has not completed QA", cover);
    }

    [Fact]
    public async Task Block_summary_lists_and_counts_included_blocks_without_counting_repeat_samples_twice()
    {
        var report = Example();
        report.Blocks = [new() { BlockId = 1, Name = "West" }, new() { BlockId = 2, Name = "East" },
            new() { BlockId = 1, Name = "West" }, new() { BlockId = 3, Name = "Excluded", Excluded = true },
            new() { BlockId = 4, Name = "East" }, new() { Name = "Older north" }, new() { Name = "Older south" }];
        var html = await Render(report);
        var summary = Regex.Match(html, "<section class=\"block-summary\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Matches("<strong[^>]*>5</strong> blocks included", summary);
        Assert.Equal(5, Regex.Matches(summary, "<li[ >]").Count);
        Assert.DoesNotContain("Excluded", summary);
        Assert.Single(Regex.Matches(summary, ">West</li>"));
        Assert.Equal(2, Regex.Matches(summary, ">East</li>").Count); // Equal labels do not merge separate saved blocks.
        Assert.Contains("Older north", summary); Assert.Contains("Older south", summary);
        Assert.True(html.IndexOf("block-summary", StringComparison.Ordinal) < html.IndexOf(">Key Actions</h3>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_tables_colour_low_results_red_and_high_results_amber_even_for_inverse_formulas(bool inverse)
    {
        var report = Example();
        report.Measures = [new() { Key = "M1", Label = "Test measure", NumberFormat = "#,##0.0" }];
        report.Blocks = [new() { Name = "Low", Values = new() { ["M1"] = new() { Value = 1, Minimum = 5, Maximum = 10, Target = 7, BelowTargetIsGood = inverse } } },
            new() { Name = "High", Values = new() { ["M1"] = new() { Value = 20, Minimum = 5, Maximum = 10, Target = 7, BelowTargetIsGood = inverse } } }];
        var html = await Render(report);
        // Assert cells, not just the legend: each value appears in both the matrix and detailed results.
        Assert.Equal(2, Regex.Matches(html, "<td class=\"status-low\"").Count);
        Assert.Equal(2, Regex.Matches(html, "<td class=\"status-high\"").Count);
        Assert.All(report.Blocks, block => Assert.Equal(inverse, block.Values["M1"].BelowTargetIsGood));
    }

    [Fact]
    public async Task Overview_filters_by_targets_on_included_blocks_and_counts_only_assessable_results()
    {
        var report = Example();
        report.Measures = [new() { Key = "Primary", Label = "Primary full name", ShortName = "Primary", NumberFormat = "#,##0" },
            new() { Key = "Later", Label = "Later block target", NumberFormat = "#,##0" },
            new() { Key = "NoTarget", Label = "Untargeted measure" }, new() { Key = "Excluded", Label = "Only excluded blocks" }];
        report.Blocks = Enumerable.Range(0, 6).Select(index => new SoilReportBlock { Name = "Sample " + index }).ToList();
        report.Blocks[0].Values["Primary"] = new() { Value = 0, Target = 7, Minimum = 5, Maximum = 10 };
        report.Blocks[1].Values["Primary"] = new() { Value = 5, Target = 7, Minimum = 5, Maximum = 10 };
        report.Blocks[2].Values["Primary"] = new() { Value = 20, Target = 7, Minimum = 5, Maximum = 10 };
        report.Blocks[3].Values["Primary"] = new() { Value = null, Target = 7, Minimum = 5, Maximum = 10 };
        report.Blocks[4].Values["Primary"] = new() { Value = 7, Target = 7 }; // A result without bounds is unrated.
        report.Blocks[4].Values["Later"] = new() { Value = 0, Target = 0, Minimum = -1, Maximum = 1 };
        report.Blocks[0].Values["NoTarget"] = new() { Value = 99 };
        report.Blocks.Add(new() { Name = "Excluded block", Excluded = true, Values = new() { ["Excluded"] = new() { Target = 1 } } });

        var matrix = Assert.Single(Matrices(await Render(report)));
        Assert.Contains("title=\"Primary full name\"", matrix);
        Assert.Matches(">Primary</span></th>", matrix);
        Assert.Contains("Later block target", matrix); // Includes targets on later blocks, including zero targets.
        Assert.DoesNotContain("Untargeted measure", matrix);
        Assert.DoesNotContain("Only excluded blocks", matrix);
        Assert.DoesNotContain("Excluded block", matrix);
        Assert.Equal(7, Regex.Matches(matrix, "<td class=\"status-missing\"").Count);
        Assert.Matches("<span[^>]*>0</span>", matrix);
        var totals = Regex.Match(matrix, "<tfoot[^>]*>(.*?)</tfoot>", RegexOptions.Singleline).Value;
        Assert.Matches(@"<strong[^>]*>2</strong><span[^>]*> / 3</span>", totals);
        Assert.Matches(@"<strong[^>]*>0</strong><span[^>]*> / 1</span>", totals);
        report.Options.ShowStatusOverview = false;
        Assert.Empty(Matrices(await Render(report)));
    }

    [Fact]
    public async Task Overview_pages_cover_both_axes_without_losing_last_blocks_or_measures()
    {
        var report = Example();
        report.Measures = report.Measures.Take(21).ToList();
        var prototype = report.Blocks[0];
        report.Blocks = Enumerable.Range(1, 28).Select(index => new SoilReportBlock { Name = "Paged block " + index,
            Values = prototype.Values.ToDictionary(pair => pair.Key, pair => pair.Value) }).ToList();
        var html = await Render(report);
        var matrices = Matrices(html);
        Assert.Equal(4, matrices.Count); // 25 + 3 blocks, each with 20 + 1 measures.
        Assert.Equal(25, Regex.Matches(matrices[0], "<th scope=\"row\"[^>]*>Paged block").Count);
        Assert.Equal(3, Regex.Matches(matrices[3], "<th scope=\"row\"[^>]*>Paged block").Count);
        Assert.Contains("Paged block 28", matrices[3]);
        Assert.Contains("Dynamic measure 21", matrices[3]);
        Assert.DoesNotContain("Dynamic measure 1</span>", matrices[3]);
        var summary = Regex.Match(html, "<section class=\"block-summary\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Equal(28, Regex.Matches(summary, "<li[ >]").Count);
        Assert.Contains("Paged block 28", summary); // The introduction must not truncate larger surveys either.
        await ExportPreview(html, "soil-report-many-blocks.html");
    }

    private static List<string> Matrices(string html) => Regex.Matches(html, "<table class=\"status-matrix\".*?</table>", RegexOptions.Singleline)
        .Select(match => match.Value).ToList();

    [Fact]
    public async Task Overview_gives_long_formatted_values_room_and_continues_within_portrait_width()
    {
        var report = Example();
        report.Measures = report.Measures.Take(20).ToList();
        var magnesium = report.Measures[5];
        magnesium.Label = "Magnesium ppm"; magnesium.NumberFormat = "#,##0";
        foreach (var block in report.Blocks) block.Values[magnesium.Key].Value = 972;
        report.Blocks[^1].Values[magnesium.Key].Value = 1458;
        var html = await Render(report);
        var matrix = Assert.Single(Matrices(html));
        Assert.Contains("1,458", matrix);
        // A wide result on a later block must size the whole column, without pushing this 20-measure set onto two pages.
        Assert.Contains("--matrix-column-units: 1.3", matrix);
        Assert.Contains("--matrix-measure-units: 20.3", matrix);
        await ExportPreview(html, "soil-overview-wide-values.html");

        foreach (var measure in report.Measures)
        {
            measure.NumberFormat = "#,##0.0";
            foreach (var block in report.Blocks) block.Values[measure.Key].Value = -1234567.8;
        }
        var pages = Matrices(await Render(report));
        Assert.True(pages.Count > 1);
        Assert.Equal(20, pages.Sum(page => Regex.Matches(page, "class=\"matrix-measure-heading\"").Count));
        foreach (var page in pages)
        {
            var width = double.Parse(Regex.Match(page, "--matrix-measure-units: ([0-9.]+)").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(32 + width * 7.5 <= 186); // Actual portrait paper budget, including the block-name column.
        }
        Assert.Contains("Dynamic measure 20", pages[^1]);
    }

    [Fact]
    public async Task Detail_distinguishes_an_off_scale_average_from_a_block_reading_still_on_the_scale()
    {
        var report = Example();
        report.Options.ShowIndividualBlockDetails = true;
        report.Measures = [new() { Key = "Boron", Label = "Boron ppm", NumberFormat = "0.0" }];
        report.Blocks = [new() { LabSoilId = 1, Name = "D2", Values = new() { ["Boron"] = new() { Value = 2.2, Minimum = 2.5, Target = 3, Maximum = 3.5 } } },
            new() { LabSoilId = 2, Name = "Comparison block", Values = new() { ["Boron"] = new() { Value = 1.2, Minimum = 2.5, Target = 3, Maximum = 3.5 } } }];
        var html = await Render(report);
        var detail = Regex.Matches(html, "<section class=\"soil-sheet block-detail-sheet\".*?</section>", RegexOptions.Singleline)[0].Value;
        var reading = Regex.Match(detail, "<tr[^>]*><th scope=\"row\"[^>]*>Boron ppm</th>.*?</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("Survey average 1.7; below the displayed scale", reading);
        Assert.Contains("D2 result 2.2", reading);
        Assert.Contains("left:10%", reading);
        Assert.Single(Regex.Matches(reading, "off-scale-arrow left")); // Only the average is off scale here.
        Assert.Contains("marker above it shows the survey average", detail);
        await ExportPreview(html, "soil-marker-explanation.html");
    }

    [Fact]
    public async Task Soil_history_retains_all_rounds_in_compact_portrait_groups()
    {
        var report = Example();
        report.Measures = report.Measures.Take(1).ToList();
        report.History = Enumerable.Range(1, 13).Select(index => new SoilReportHistoryRound { SurveyId = 200 + index,
            Date = new DateTime(2026, 9, 15).AddMonths(-index), BlockCount = index, Averages = new() { ["M1"] = index } }).ToList();
        var html = await Render(report);
        Assert.Equal(2, Regex.Matches(html, ">Historical Farm Averages</h2>").Count);
        Assert.Contains("maximum of three years", html);
        Assert.DoesNotContain("Three-year soil history", html);
        var tables = Regex.Matches(html, "<table class=\"history-table\".*?</table>", RegexOptions.Singleline);
        Assert.Equal(2, tables.Count);
        Assert.Equal(10, Regex.Matches(tables[0].Value, "class=\"history-date-heading\"").Count);
        Assert.Equal(3, Regex.Matches(tables[1].Value, "class=\"history-date-heading\"").Count);
        Assert.Contains("#213", tables[0].Value); // Columns are always chronological, even in older saved reports.
        Assert.Contains("13 samples", tables[0].Value);
        Assert.Contains("#201", tables[1].Value);
        Assert.All(tables.Cast<Match>(), table => Assert.Contains("class=\"history-trend\"", table.Value));
        Assert.Contains("Trends cover the full period", html);
        Assert.Equal(13, SoilHistoryTrend.Points(report.History, "M1").Count); // Continuations must not truncate the trend.
        await ExportPreview(html, "soil-report-history.html");
    }

    [Theory]
    [InlineData(0, 0, true, false)]
    [InlineData(1, 0, true, false)]
    [InlineData(2, 0, true, false)]
    [InlineData(2, 1, true, true)]
    [InlineData(2, 1, false, false)]
    public async Task Farm_history_requires_two_distinct_sampling_dates_and_an_enabled_section(
        int roundCount, int daysBetweenRounds, bool showHistory, bool expectedVisible)
    {
        var report = Example();
        report.Options.ShowHistory = showHistory;
        // Different survey IDs, times and multiple blocks on the same day must not create comparison pages.
        report.History = Enumerable.Range(0, roundCount).Select(index => new SoilReportHistoryRound
        {
            SurveyId = 7200 + index, Date = new DateTime(2026, 8, 11).AddDays(index * daysBetweenRounds).AddHours(index),
            BlockCount = 7, Averages = report.Measures.ToDictionary(measure => measure.Key, _ => 7.5)
        }).ToList();

        var html = await Render(report);

        Assert.Equal(expectedVisible, html.Contains(">Historical Farm Averages</h2>"));
        Assert.Equal(expectedVisible, html.Contains("class=\"history-table\""));
        Assert.Contains(">Block Results</h2>", html);
        Assert.Equal(roundCount, report.History.Count); // Only presentation changes; saved averages remain available.
        Assert.Equal(showHistory, report.Options.ShowHistory);
    }

    [Fact]
    public async Task Shared_ranges_do_not_hide_block_specific_targets_that_round_to_the_same_display_value()
    {
        var report = Example();
        report.Measures = [new() { Key = "BoronPpm", Label = "Boron ppm", NumberFormat = "0" }];
        report.Blocks = [new() { Name = "North", Values = new() { ["BoronPpm"] = new() { Value = 2, Minimum = 1, Target = 3, Maximum = 5 } } },
            new() { Name = "South", Values = new() { ["BoronPpm"] = new() { Value = 2, Minimum = 1, Target = 3, Maximum = 5 } } }];
        var shared = await Render(report);
        Assert.Contains("reference-values", shared);
        Assert.DoesNotContain("class=\"result-target\"", shared);
        // A CEC-driven target can differ even when the configured display format rounds both numbers alike.
        report.Blocks[1].Values["BoronPpm"].Target = 3.1;
        var varying = await Render(report);
        Assert.Contains("Varies by block", varying);
        Assert.Equal(2, Regex.Matches(varying, "class=\"result-target\"").Count);
    }

    [Fact]
    public async Task Historical_trends_keep_gaps_and_leave_unavailable_or_single_results_blank()
    {
        var report = Example();
        report.Measures = new[] { "Increasing", "Flat", "Missing", "Single", "Unavailable" }
            .Select(key => new SoilReportMeasure { Key = key, Label = key, NumberFormat = "0.0" }).ToList();
        report.History = Enumerable.Range(0, 5).Select(index => new SoilReportHistoryRound
        {
            SurveyId = 300 + index, Date = new DateTime(2024, 1, 1).AddMonths(index * 6), BlockCount = 6,
            Averages = new() { ["Increasing"] = index * 2, ["Flat"] = 0 }
        }).ToList();
        foreach (var index in new[] { 0, 1, 3, 4 }) report.History[index].Averages["Missing"] = index;
        report.History[2].Averages["Single"] = 5;
        var html = await Render(report);
        var table = Regex.Match(html, "<table class=\"history-table\".*?</table>", RegexOptions.Singleline).Value;
        Assert.Equal(3, Regex.Matches(table, "class=\"history-trend\"").Count);
        Assert.Contains("&quot;val&quot;:[0,1,null,3,4]", table);
        Assert.Contains("&quot;ignoreEmptyPoints&quot;:false", table);
        await ExportPreview(html, "soil-report-history-trend-cases.html");
    }

    [Fact]
    public async Task Saved_revision_preserves_case_insensitive_measure_bindings_and_real_footer_values()
    {
        var report = Example();
        report.AgronomistPhone = "0400 123 456";
        report.Measures = [new() { Key = "Nh4nPpm", Label = "NH4-N ppm", NumberFormat = "0" }];
        report.Blocks = [new() { Name = "Block A", Values = new() { ["Nh4nppm"] = new() { Value = 3 } } }];
        report.History = [new() { Averages = new() { ["nh4nppm"] = 2 } }];
        // Reproduce both the database snapshot and HTTP serialization boundaries used by the real report.
        var saved = JsonSerializer.Deserialize<SoilReportDocument>(JsonSerializer.Serialize(report))!;
        Assert.Equal(3, saved.Blocks[0].Values["Nh4nPpm"].Value);
        Assert.Equal(2, saved.History[0].Averages["Nh4nPpm"]);
        var html = await Render(saved);
        Assert.DoesNotContain("Document.PropertyName", html);
        Assert.DoesNotContain("Document.Agronomist", html);
        Assert.Contains("Prepared by Example agronomist  P: 0400 123 456", html);
        Assert.Contains("total-grower-services.png", html);
        Assert.Contains("NH4-N ppm", html);
        Assert.Matches(@"<strong[^>]*>\s*3</strong>", html);
    }

    [Fact]
    public async Task Repeated_review_warnings_keep_each_rule_and_affected_block_without_repeating_every_message()
    {
        var report = Example();
        report.Blocks[0].Name = "North: Block";
        foreach (var block in report.Blocks)
        {
            report.Warnings.Add($"{block.Name}: Aluminium formula needs review.");
            report.Warnings.Add($"{block.Name}: Hydrogen formula needs review.");
        }
        report.Warnings.Add("North: Block: A sample-specific warning.");
        report.Warnings.Add("General report warning.");
        var html = await Render(report);
        Assert.Single(Regex.Matches(html, "Aluminium formula needs review"));
        Assert.Single(Regex.Matches(html, "Hydrogen formula needs review"));
        Assert.Contains("Applies to: all 9 blocks", html);
        Assert.Contains("Applies to: North: Block", html);
        Assert.Contains("General report warning.", html);
        Assert.Equal(20, report.Warnings.Count); // Rendering never rewrites the frozen audit evidence.
    }

    [Fact]
    public async Task Dynamic_rows_actions_and_block_groups_render_with_portrait_print_rules()
    {
        var document = Example();
        var html = await Render(document);
        Assert.Contains("size: A4 portrait", html);
        Assert.DoesNotContain("size: A4 landscape", html);
        Assert.Contains("Tailored action heading", html);
        Assert.Contains("Dynamic measure 25", html);
        Assert.Contains("Block 9", html);
        Assert.Contains("status-low", html); Assert.Contains("status-high", html);
        Assert.Equal(6, Regex.Matches(html, ">Block Results</h2>").Count); // Three block groups × two compact measure groups.
        // Every requirements page precedes every balancing page, including the last block group.
        var requirements = Regex.Matches(html, ">Theoretical Elemental Requirements</h2>");
        var balancing = Regex.Matches(html, ">Soil Balance Recommendations</h2>");
        Assert.Equal(4, requirements.Count); // Six + three blocks, with the same 24 + one measure rows.
        Assert.Equal(2, balancing.Count);
        Assert.True(requirements[^1].Index < balancing[0].Index);
        // Both amount tables retain every block across the new six-column boundary and continuation pages.
        foreach (var kind in new[] { "requirements", "balancing" })
        {
            var tables = Regex.Matches(html, $"<table class=\"block-amounts {kind}-table\".*?</table>", RegexOptions.Singleline);
            var firstHead = Regex.Match(tables[0].Value, "<thead[^>]*>.*?</thead>", RegexOptions.Singleline).Value;
            var lastHead = Regex.Match(tables[^1].Value, "<thead[^>]*>.*?</thead>", RegexOptions.Singleline).Value;
            Assert.Equal(7, Regex.Matches(firstHead, "<th[ >]").Count); // Label plus six blocks.
            Assert.Contains("Block 6", firstHead);
            Assert.DoesNotContain("Block 7", firstHead);
            Assert.Equal(4, Regex.Matches(lastHead, "<th[ >]").Count); // Label plus remaining three blocks.
            Assert.Contains("Block 7", lastHead);
            Assert.Contains("Block 9", lastHead);
        }
        // The repeated laboratory footnote is removed; the actual report disclaimer remains online and in print rules.
        Assert.DoesNotContain("Example laboratory", html);
        Assert.Contains("<footer class=\"survey-report-footer\"", html);
        Assert.Contains("expressly disclaims any liability", html);
        Assert.Contains("Theoretical Elemental Requirements", html);
        Assert.Contains("Application note for selected crops", html);
        document.Options.ShowBlockDetails = false; document.Options.ShowRequiredAmounts = false;
        var reduced = await Render(document);
        Assert.DoesNotContain(">Block Results</h2>", reduced);
        Assert.DoesNotContain(">Theoretical Elemental Requirements</h2>", reduced);
        Assert.Contains("Application note for selected crops", reduced);
        await ExportPreview(html, "soil-report.html");
    }

    [Fact]
    public async Task Individual_pages_use_saved_definitions_final_rates_and_only_included_blocks()
    {
        var report = Example();
        report.Options.ShowIndividualBlockDetails = true;
        report.Blocks = report.Blocks.Take(2).ToList();
        report.Blocks[1].Excluded = true;
        report.Blocks[0].Recommendations[0].Rate = 250;
        report.Blocks[0].Recommendations[0].Overridden = true;
        report.Blocks[0].Recommendations[0].Reason = "Split application after review.";
        report.Blocks[0].Definition = "custom";
        report.DefinitionSnapshot = [new() { Key = "custom", Sections = [new() { Name = "Custom section", Items = [
            new() { Field = "M1", Label = "Custom nutrient ppm", NumberFormat = "0.0" },
            new() { Field = "Summary", Label = "Untargeted observation", NumberFormat = "0.0" },
            new() { Field = "Missing", Label = "Unavailable observation", NumberFormat = "0.0" }] }] }];
        report.Blocks[0].Values["Summary"] = new() { Value = 3.5 };
        var html = await Render(report);
        var detail = Assert.Single(Regex.Matches(html, "<section class=\"soil-sheet block-detail-sheet\".*?</section>", RegexOptions.Singleline)).Value;
        Assert.Contains("Custom nutrient ppm", detail);
        Assert.DoesNotContain("Dynamic measure", detail);
        Assert.DoesNotContain("Block 2", detail);
        Assert.Contains("250 *", detail);
        Assert.Contains("Split application after review.", detail);
        Assert.Contains("Untargeted observation", detail);
        Assert.Contains("Unavailable observation", detail);
        Assert.Contains("Avg 3.0", detail); // Excluded recommendations still contribute to the survey average.
        Assert.Contains("block-detail-link", html);
        // Rendering is read-only, including the edited recommendation and source values.
        Assert.Equal(600, report.Blocks[0].Recommendations[0].CalculatedRate);
        Assert.Equal(2, report.Blocks[0].Values["M1"].Value);
        report.Options.ShowIndividualBlockDetails = false;
        Assert.DoesNotContain("block-detail-sheet", await Render(report));
    }

    [Fact]
    public async Task Block_detail_continuations_retain_every_measure_and_guidance_in_portrait_output()
    {
        var report = Example();
        report.Options.ShowIndividualBlockDetails = true;
        report.Blocks = report.Blocks.Take(2).ToList();
        report.Blocks[0].Name = "Northern orchard — upper irrigation section";
        foreach (var block in report.Blocks)
        {
            block.SampleDate = report.SurveyDate;
            block.Recommendations.Add(new() { Product = "Gypsum", Rate = 1200, CalculatedRate = 1000, Overridden = true, Reason = "Apply in two passes after irrigation." });
            foreach (var (key, label, number) in new[] { ("Texture", "Texture index", 4d), ("Capacity", "Exchange capacity", 18d), ("Density", "Bulk density", 1.2), ("Carbon", "Organic carbon %", 2.5) })
            {
                if (!report.Measures.Any(item => item.Key == key)) report.Measures.Insert(0, new() { Key = key, Label = label, NumberFormat = "0.0" });
                block.Values[key] = new() { Value = number };
            }
            report.Notes.Add(new() { Title = "Block application advice", RuleId = "test-" + block.LabSoilId, Text = "Apply products evenly across the sampled area. Review soil moisture and discuss the timing with your agronomist.", LabSoilIds = [block.LabSoilId] });
        }
        // Exercise an overflow arrow and an unavailable reading without synthesising a zero.
        report.Blocks[0].Values["M1"].Value = 500;
        report.Blocks[0].Values["M2"].Value = null;
        report.Blocks[1].Values["M1"].Value = -500;
        var html = await Render(report);
        var details = Regex.Matches(html, "<section class=\"soil-sheet block-detail-sheet\".*?</section>", RegexOptions.Singleline);
        Assert.True(details.Count >= 4);
        Assert.Equal(2, Regex.Matches(string.Join("", details), "Dynamic measure 25</th>").Count);
        Assert.Contains("detail-marker status-high right", html);
        Assert.Contains("detail-marker status-low left", html);
        Assert.Contains("off-scale-arrow right", html); Assert.Contains("off-scale-arrow left", html);
        Assert.DoesNotContain("◀", html); Assert.DoesNotContain("▶", html);
        Assert.Contains("Block application advice", details[^1].Value);
        Assert.Contains("size: A4 portrait", html);
        await ExportPreview(html, "soil-report-block-details.html");
    }

    private static async Task ExportPreview(string html, string fileName)
    {
        // Optional export is for manual browser/print QA; it contains synthetic data only.
        var directory = Environment.GetEnvironmentVariable("METAGROW_SOIL_PREVIEW_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, fileName), "<!doctype html><html data-bs-theme=\"light\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Soil report — synthetic layout check</title><link rel=\"stylesheet\" href=\"theme.css\"><link rel=\"stylesheet\" href=\"report.css\"><style>body{margin:0;padding:1rem;font-family:Arial,sans-serif} .preview-tools{max-width:920px;margin:auto;display:flex;gap:1rem} @media print{.preview-tools{display:none}body{padding:0}}</style></head><body><nav class=\"preview-tools\"><a href=\"soil-report-dark.html\">Dark theme</a><a href=\"soil-report-many-blocks.html#results\">Many blocks</a><a href=\"#results\">Results</a><span>Synthetic test data</span></nav>" + html.Replace(">Status Overview</h2>", " id=\"results\">Status Overview</h2>") + "</body></html>");
        }
    }

    private static async Task<string> Render(SoilReportDocument document)
    {
        var collection = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoJavaScript>();
        // Historical rows now use the same DevExpress services as the interactive/shared report host.
        collection.AddDevExpressBlazor();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<SoilReportContent>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["Document"] = document, ["BrandKey"] = ReportBranding.TotalGrowerServicesKey }))).ToHtmlString());
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        // Static rendering must not invoke browser code; navigation runs only on a user click.
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException(identifier);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException(identifier);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
    }

    private static SoilReportDocument Example()
    {
        var report = new SoilReportDocument { SurveyId = 100, Revision = 3, Status = "Approved", PropertyName = "Layout test farm", SurveyDate = new(2026, 9, 15), Agronomist = "Example agronomist", TemplateName = "Soil cropping",
            Actions = [new() { Title = "Tailored action heading", Text = "Apply the recommended products when soil conditions are suitable.\nReview crop response with the agronomist before the next application." }, new() { Title = "Organic matter", Text = "Maintain ground cover and monitor organic carbon over time." }] };
        for (var measure = 1; measure <= 25; measure++) report.Measures.Add(new() { Key = "M" + measure, Label = "Dynamic measure " + measure, NumberFormat = "#,##0.0", Section = measure <= 8 ? "Soil condition" : measure <= 18 ? "Nutrients" : "Exchange balance", SectionColour = "#639f43" });
        for (var index = 1; index <= 9; index++)
        {
            var block = new SoilReportBlock { LabSoilId = index, BlockId = index, Name = "Block " + index, Crop = index < 5 ? "Avocado" : "Banana", SampleReference = "1234" + index, Laboratory = "Example laboratory", SampleType = "Soil" };
            foreach (var measure in report.Measures) { block.Values[measure.Key] = new() { Value = index * 2, Minimum = 5, Maximum = 12, Target = 9 }; block.Requirements.Add(new() { Key = measure.Key, Name = measure.Label, Amount = index * 10 }); }
            block.Recommendations.Add(new() { Product = "Lime", CalculatedRate = 600, Rate = 600 }); report.Blocks.Add(block);
        }
        report.Notes.Add(new() { RuleId = "application", Title = "Application note for selected crops", Text = "Any boron recommendations are based on applications covering the entire area. Decrease quantities when treating only part of the area.", LabSoilIds = [5, 6, 7, 8, 9] });
        report.History.Add(new() { Date = new(2025, 9, 15), SurveyId = 99, BlockCount = 9, Averages = report.Measures.ToDictionary(item => item.Key, item => 7.5) });
        return report;
    }
}
