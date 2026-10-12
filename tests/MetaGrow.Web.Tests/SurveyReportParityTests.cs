using ApiModels;
using DevExpress.Blazor;
using MetaGrow.Web.Components.Pages;
using MetaGrow.Web.Components.Shared;
using MetaGrow.Web.Services;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net;

namespace MetaGrow.Web.Tests;

public class SurveyReportParityTests
{
    [Fact]
    public void Chart_transpose_preserves_block_order_and_each_measurement()
    {
        var section = new MultiCropReportSectionDto
        {
            Metrics = [new() { MetricCode = "pest", MetricLabel = "Pest", SortOrder = 1 }, new() { MetricCode = "benefit", MetricLabel = "Beneficial", SortOrder = 2 }],
            Series = [
                new() { BlockId = 12, Name = "AR 11+12", Points = [new() { MetricCode = "pest", Value = 6 }, new() { MetricCode = "benefit", Value = 2 }] },
                new() { BlockId = 13, Name = "AR 13+14", Points = [new() { MetricCode = "benefit", Value = 4 }, new() { MetricCode = "pest", Value = 8 }] }
            ]
        };
        var chart = ReportChartLayout.WithBlocksOnAxis(section);
        Assert.Equal(["AR 11+12", "AR 13+14"], chart.Metrics.Select(m => m.MetricLabel));
        Assert.Equal([6d, 8d], chart.Series[0].Points.Select(p => p.Value!.Value));
        Assert.Equal([2d, 4d], chart.Series[1].Points.Select(p => p.Value!.Value));
    }

    [Theory]
    [InlineData(24.51, "SE", "25")]
    [InlineData(157.88, "BunchFingerCount", "157.9")]
    [InlineData(8.75, "Prod", "8.8")]
    [InlineData(33.33, "NematodeDamageOnRoots", "33.33")]
    public void Banana_values_use_legacy_precision(double value, string code, string expected) =>
        Assert.Equal(expected, ReportChartLayout.BananaValue(value, code));

    [Fact]
    public async Task Multicrop_print_orders_observations_actions_charts_summary_checklist_history()
    {
        var report = new MultiCropOnlineReportDto
        {
            Comments = [new() { Title = "Plant health", Text = "Read this first" }],
            Blocks = [new() { BlockId = 1, BlockName = "AR 11+12", ShortCode = "D5+6", SummaryValues = [new() { MetricCode = "p", MetricLabel = "Pest", Value = 2 }] }],
            Actions = [new() { ActionGroupId = 1, ActionGroupName = "Nutrition", ActionName = "Feed", BlockIds = [1] }],
            Sections = [new() { Code = "pests-beneficials", Title = "Pests and beneficials", Metrics = [new() { MetricCode = "p", MetricLabel = "Pest" }], Series = [new() { BlockId = 1, Name = "AR 11+12", Points = [new() { MetricCode = "p", Value = 2 }] }] },
                new() { Code = "disease", Title = "Disease" },
                new() { Code = "history", Title = "Historical measurements", IsHistorical = true }]
        };
        var html = await Render<MultiCropReport>(report);
        Ordered(html, "observations-title", "actions-1", "section-pests-beneficials", "section-disease", "summary-title", "action-checklist-title", "section-history");
        Assert.Contains("No disease found during this survey.", html);
        Assert.DoesNotContain("No pests or beneficials were found", html);
        Assert.Contains("AR 11+12", html);
        Assert.DoesNotContain("D5+6", html);
    }

    [Fact]
    public async Task Banana_print_orders_actions_history_rust_summary_checklist_optional_sections()
    {
        var report = new BananaOnlineReportDto
        {
            Comments = [new() { Title = "Notes", Text = "Read this first" }],
            Actions = [new() { ActionGroupId = 1, Recommendation = "Feed", Blocks = "A" }],
            Charts = [new() { Code = "mites-beneficials", Title = "Mites" }, new() { Code = "sigatoka-lesions", Title = "Lesions", IsHistorical = true }, new() { Code = "leaf-emergence-rate", Title = "LER", IsHistorical = true }],
            RustThrips = new() { Dates = [new DateTime(2026, 9, 3)], Rows = [new() { BlockName = "A", Values = ["P"] }] },
            Tables = [new() { Code = "nematode", Title = "Nematode" }, new() { Code = "soil-health", Title = "Soil health" }, new() { Code = "bwb", Title = "BWB" }, new() { Code = "pest-summary", Title = "Pest summary", Rows = [new() { Label = "A" }] }]
        };
        var html = await Render<BananaReport>(report);
        Ordered(html, "banana-actions-title", "banana-chart-leaf-emergence-rate", "banana-chart-sigatoka-lesions", "banana-chart-mites-beneficials", "rust-thrips-history-title", "banana-table-pest-summary", "banana-action-checklist-title", "banana-table-bwb", "banana-table-soil-health", "banana-table-nematode");
    }

    [Fact]
    public async Task Banana_actions_keep_all_categories_and_both_action_pages_have_no_survey_context()
    {
        var report = new BananaOnlineReportDto {
            Actions = [new() { ActionGroupName = "Nutrition", Recommendation = "Feed", Blocks = "A" },
                new() { ActionGroupName = "Irrigation", Recommendation = "Water", Blocks = "A" },
                new() { ActionGroupName = "Spray", Recommendation = "Spray action", Blocks = "A" }]
        };
        var html = await Render<BananaReport>(report);
        var actions = html[html.IndexOf("id=\"banana-actions-title\"", StringComparison.Ordinal)..html.IndexOf("class=\"chart-pairs\"", StringComparison.Ordinal)];
        Assert.Contains(">Actions</h2>", actions);
        Assert.Contains("Nutrition", actions); Assert.Contains("Irrigation", actions); Assert.Contains("Spray action", actions);
        Assert.DoesNotContain("handout-context", actions);
        var checklist = html[html.IndexOf("id=\"banana-action-checklist-title\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("handout-context", checklist);
    }

    [Fact]
    public async Task Rust_thrips_continuation_preserves_every_date_and_its_cell()
    {
        var dates = Enumerable.Range(0, 43).Select(i => new DateTime(2026, 10, 12).AddDays(-i)).ToList();
        var report = new BananaOnlineReportDto { RustThrips = new() { Dates = dates,
            Rows = [new() { BlockName = "A", Values = dates.Select((_, i) => "Cell" + i).ToList() }] } };
        var html = await Render<BananaReport>(report);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "<table class=\"summary-table rust-thrips-table\"").Count);
        foreach (var (date, index) in dates.Select((d, i) => (d, i)))
        {
            Assert.Contains(">" + date.ToString("dd/MM/yy") + "</span></th>", html);
            Assert.Contains(">Cell" + index + "</td>", html);
        }
    }

    [Fact]
    public void Summary_group_spans_keep_overflow_after_legacy_columns()
    {
        var columns = new[] { "Growth", "Growth", "Pests", "Beneficials", "Disease", "Growth", "Pests" }
            .Select(g => new MultiCropReportMetricValueDto { SummaryGroup = g }).ToList();
        Assert.Equal(new[] { ("Growth", 2), ("Pests", 1), ("Beneficials", 1), ("Disease", 1), ("Growth", 1), ("Pests", 1) }, ReportChartLayout.SummaryGroups(columns));
        Assert.Equal("Tree Crop Survey", ReportChartLayout.SurveyTitle("TGS Tree Crop Survey"));
        Assert.Equal(new[] { "First", "Second", "Third" }, ReportChartLayout.ObservationLines("First\r\n• Second\n\n- Third"));
    }

    [Theory]
    [InlineData(ReportBranding.MetagenKey, "Metagen Pty Ltd")]
    [InlineData(ReportBranding.TotalGrowerServicesKey, "TGS Pty Ltd")]
    public async Task Footer_uses_selected_company_and_native_total_page_counter(string brand, string company)
    {
        var html = await RenderParameters<SurveyReportFooter>(new() {
            ["Brand"] = ReportBranding.Get(brand), ["PropertyName"] = "Farm </style><script>unsafe</script>",
            ["AgronomistName"] = "Test agronomist", ["AgronomistPhone"] = "123456", ["Reference"] = "2026-1",
            ["SurveyDate"] = new DateTime(2026, 9, 3)
        });
        Assert.Contains(company + " does not make any representations", html);
        Assert.Contains("counter(page)", html); Assert.Contains("counter(pages)", html);
        Assert.Contains("Prepared by Test agronomist", html);
        Assert.Contains("Survey date: " + new DateTime(2026, 9, 3).ToString("dd MMM yyyy"), html);
        Assert.DoesNotContain("<script>", html);
    }

    [ChromeTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Printing_report_inside_app_shell_has_no_blank_boundary_pages(bool banana)
    {
        var reportHtml = banana
            ? await Render<BananaReport>(new BananaOnlineReportDto { PropertyName = "Print regression farm", SurveyDate = new(2026, 9, 15) })
            : await Render<MultiCropReport>(new MultiCropOnlineReportDto { PropertyName = "Print regression farm", SurveyDate = new(2026, 9, 15) });
        // Even a small amount of layout outside a named-page report creates
        // separate default pages before and after it in Chromium.
        var html = "<!doctype html><html><head><style>body{margin:0;font:12px Arial}.app-shell{padding:1px 0}" +
            "@media print{.no-print,.survey-report-footer{display:none!important}}</style></head><body>" +
            "<main class=\"app-shell\">" + reportHtml + "</main></body></html>";
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.Combine(temporaryRoot, "metagrow-print-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "report.html");
            var output = Path.Combine(directory, "report.pdf");
            await File.WriteAllTextAsync(input, html);
            var start = new ProcessStartInfo(ChromeTheoryAttribute.ChromePath)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in new[] { "--headless", "--disable-gpu", "--no-pdf-header-footer", "--no-first-run",
                "--disable-extensions", "--disable-background-networking", "--user-data-dir=" + Path.Combine(directory, "profile"),
                "--print-to-pdf=" + output, new Uri(input).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.Equal(0, process.ExitCode);
            var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output));
            Assert.Single(Regex.Matches(pdf, @"/Type\s*/Page\b"));
        }
        finally
        {
            // Delete only this test's uniquely named directory beneath the temp root.
            if (Path.GetFullPath(directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { /* Chromium can release its profile asynchronously. */ }
            }
        }
    }

    [Fact]
    public async Task Banana_cover_shows_observations_heading_and_empty_message_without_leaf_record_count()
    {
        var report = new BananaOnlineReportDto { LeafCount = 999 };
        var html = await Render<BananaReport>(report);
        Assert.Contains(">Observations</h2>", html);
        Assert.Contains("No observations were recorded for this survey.", html);
        Assert.DoesNotContain("Leaf records", html);
        report.Comments = [new() { Title = "Agronomist comments", Text = "Keep this field note" }];
        html = await Render<BananaReport>(report);
        Assert.Contains("Keep this field note", html);
        Assert.DoesNotContain("No observations were recorded", html);
    }

    [Fact]
    public async Task Printable_charts_have_horizontal_labels_fixed_percent_axis_and_dashed_average()
    {
        var section = new MultiCropReportSectionDto { Title = "Growth", Metrics = [new() { MetricCode = "A", MetricLabel = "Block A" }],
            Series = [new() { Name = "Flowering", Points = [new() { MetricCode = "A", Value = 80 }] }] };
        var html = await RenderParameters<MultiCropPrintChart>(new() { ["Section"] = section, ["FixedAxisMaximum"] = 100d, ["PercentageAxis"] = true });
        Assert.Contains(">100%</text>", html); Assert.DoesNotContain("rotate(", html);
        Assert.Contains("class=\"bar-value\"", html);
        section.IsHistorical = true;
        section.Series = [new() { Name = "Area average", Points = [new() { Date = new DateTime(2026, 1, 1), Value = 5 }, new() { Date = new DateTime(2026, 2, 1), Value = 6 }] }];
        html = await RenderParameters<MultiCropPrintChart>(new() { ["Section"] = section });
        Assert.Contains("polyline class=\"history-line area-average\"", html);
        Assert.DoesNotContain("circle class=\"history-point\"", html);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(3d)]
    [InlineData(5d)]
    public async Task Online_soil_moisture_axis_keeps_zero_to_five_without_padding(double value)
    {
        var report = new BananaOnlineReportDto
        {
            Charts = [new() { Code = "soil-moisture", Title = "Soil moisture", Series = [
                new() { Name = "Moisture", Points = [new() { Category = "Block A", Value = value }] }] }]
        };
        var html = await Render<BananaReport>(report);
        var attribute = Regex.Match(html, "<dxbl-chart[^>]*option-chunks=\"([^\"]*)\"");
        Assert.True(attribute.Success);
        using var chunks = JsonDocument.Parse(WebUtility.HtmlDecode(attribute.Groups[1].Value));
        var axis = chunks.RootElement.EnumerateObject()
            .Where(chunk => chunk.Value.TryGetProperty("valueAxis", out _))
            .SelectMany(chunk => chunk.Value.GetProperty("valueAxis").EnumerateArray())
            .Last(valueAxis => valueAxis.TryGetProperty("visualRange", out _));
        Assert.Equal(0d, axis.GetProperty("visualRange").GetProperty("startValue").GetDouble());
        Assert.Equal(5d, axis.GetProperty("visualRange").GetProperty("endValue").GetDouble());
        Assert.False(axis.GetProperty("valueMarginsEnabled").GetBoolean());
        Assert.False(axis.GetProperty("endOnTick").GetBoolean());
        Assert.Equal(1d, axis.GetProperty("tickInterval").GetDouble());
        Assert.Equal("keep", axis.GetProperty("visualRangeUpdateMode").GetString());
    }

    private static void Ordered(string html, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var index = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > previous, $"Missing or misplaced section {marker}");
            previous = index;
        }
    }

    private static Task<string> Render<T>(object report) where T : IComponent =>
        RenderParameters<T>(new() { ["Report"] = report, ["Shared"] = true });

    private static async Task<string> RenderParameters<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton(DispatchProxy.Create<ITgsApiService, EmptyProxy>());
        collection.AddSingleton<IJSRuntime, NoJavaScript>();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
    public class EmptyProxy : DispatchProxy { protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(); }
    private class TestNavigation : NavigationManager { public TestNavigation() => Initialize("http://localhost/", "http://localhost/"); }
    private class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}

public sealed class ChromeTheoryAttribute : TheoryAttribute
{
    public static string ChromePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");

    public ChromeTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(ChromePath))
            Skip = "The report pagination regression requires Chrome on Windows.";
    }
}
