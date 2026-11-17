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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Report_map_uses_actual_property_and_displays_dated_recommendation_with_blocks(bool banana)
    {
        var data = SurveyActionMapTests.Fixture(banana ? "banana" : "multicrop");
        data.Recommendations[0].Summary = "23/10 Setbest recommended";
        var html = banana
            ? await Render<BananaReport>(new BananaOnlineReportDto { PropertyName = "River Farm", ActionMaps = data })
            : await Render<MultiCropReport>(new MultiCropOnlineReportDto { PropertyName = "River Farm", ActionMaps = data });
        Assert.DoesNotContain("_report.PropertyName", html);
        Assert.Matches("class=\"map-context\"[^>]*>River Farm", html);
        var key = Regex.Match(html, "class=\"map-legend\".*?</article>", RegexOptions.Singleline).Value;
        Assert.Contains("23/10 Setbest recommended", key);
        Assert.Contains("Block A (1.10 ha), Block B (1.10 ha)", key);
        // Keep geographic measurements in the key so both online and printed SVG labels stay short.
        var svg = Regex.Match(html, "<svg[^>]*class=\"map-canvas\".*?</svg>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(svg);
        Assert.Matches(">Block A</tspan>", svg);
        Assert.DoesNotContain(" ha", svg);
        Assert.Contains("Total area:", key);
        Assert.Contains("2.20 ha", key);
        // The banana report already provides the same information in its Actions section.
        Assert.Equal(!banana, html.Contains("Work summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_kml_is_a_collapsed_online_disclosure_and_work_lists_affected_blocks()
    {
        var data = SurveyActionMapTests.Fixture();
        data.Blocks[1].Kml.Clear();
        var html = await RenderParameters<SurveyActionMaps>(new()
        {
            ["Data"] = data, ["SurveyType"] = "banana", ["ReadOnly"] = true
        });
        var disclosure = Regex.Match(html, "<details[^>]*class=\"map-notice no-print\"[^>]*>.*?</details>", RegexOptions.Singleline);
        Assert.True(disclosure.Success);
        Assert.DoesNotContain(" open", disclosure.Value.Split('>')[0]);
        Assert.Contains("Blocks without usable KML (1)", disclosure.Value);
        Assert.Matches("<li[^>]*>Block B</li>", disclosure.Value);
        Assert.Contains("Work summary", html);
        Assert.Contains("Block A (1.10 ha), Block B (area unavailable)", html);
        Assert.Contains("map-canvas", html);
    }

    [Fact]
    public async Task Action_map_switches_remove_map_content_and_preserve_default_visibility()
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton(DispatchProxy.Create<ITgsApiService, EmptyProxy>());
        collection.AddSingleton<IJSRuntime, NoJavaScript>();
        collection.AddSingleton<IComponentActivator, MapComponentActivator>();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        await using var services = collection.BuildServiceProvider();
        var activator = (MapComponentActivator)services.GetRequiredService<IComponentActivator>();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SurveyActionMaps>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Data"] = SurveyActionMapTests.Fixture(), ["SurveyType"] = "banana", ["ReadOnly"] = true
            }));
            Assert.Contains("map-canvas", output.ToHtmlString());
            var component = activator.Map!;
            var view = (MetaGrow.Web.Models.SurveyActionMapView)typeof(SurveyActionMaps).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var render = typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // These are the values bound to the two DevExpress switch controls.
            view.Maps[0].Visible = false;
            render.Invoke(component, null);
            Assert.DoesNotContain("map-canvas", output.ToHtmlString());
            Assert.Contains("maps-print-hidden", output.ToHtmlString());
            Assert.Contains("Work summary", output.ToHtmlString());
            view.Maps[0].Visible = true;
            typeof(SurveyActionMaps).GetField("_showMaps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, false);
            render.Invoke(component, null);
            Assert.DoesNotContain("map-canvas", output.ToHtmlString());
            typeof(SurveyActionMaps).GetField("_showMaps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, true);
            render.Invoke(component, null);
            Assert.Contains("map-canvas", output.ToHtmlString());
        });
    }

    private sealed class MapComponentActivator(IServiceProvider _services) : IComponentActivator
    {
        public SurveyActionMaps? Map { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)ActivatorUtilities.CreateInstance(_services, type);
            if (component is SurveyActionMaps map) Map = map;
            return component;
        }
    }

    [ChromeTheory]
    [InlineData("banana")]
    [InlineData("multicrop")]
    public async Task Action_map_and_legend_print_together_without_clipping(string type)
    {
        var data = SurveyActionMapTests.Fixture(type);
        // Exercise a realistic multi-entry key on the same landscape paper as the main reports.
        for (var index = 3; index <= 8; index++)
        {
            data.Blocks.Add(new() { BlockId = index, BlockType = type, BlockName = $"Block {index}", Kml = [.. data.Blocks[0].Kml] });
            data.Recommendations.Add(new() { SurveyActionId = index + 20, ActionGroupId = 1, ActionGroupName = "Nutrition",
                Summary = $"23/10 Recommendation {index}", BlockIds = [index] });
        }
        // Force an outside callout and verify it shares the printed map sheet with the complete key.
        data.Blocks[^1].BlockName = "Very long block name with a complete field instruction";
        data.Blocks[^1].Kml = [data.Blocks[0].Kml[0].Replace("153.001", "153.000001").Replace("-27.001", "-27.000001")];
        // Even a long, expanded diagnostic disclosure must consume no printed space.
        data.Blocks[1].Kml.Clear();
        data.Warnings = Enumerable.Range(1, 200).Select(number => $"Unavailable boundary {number}").ToList();
        var html = await RenderParameters<SurveyActionMaps>(new()
        {
            ["Data"] = data, ["SurveyType"] = type,
            ["SurveyId"] = 17, ["PropertyName"] = "Map verification farm", ["SurveyDate"] = new DateTime(2026, 10, 21), ["ReadOnly"] = true
        });
        Assert.Contains("class=\"map-label-leader\"", html);
        html = html.Replace("<details ", "<details open ");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MetaGrow.Web"))) root = root.Parent;
        Assert.NotNull(root);
        var cssPath = Path.Combine(root.FullName, "src", "MetaGrow.Web", "obj", "Debug", "net10.0", "scopedcss", "bundle", "MetaGrow.Web.styles.css");
        var css = await File.ReadAllTextAsync(cssPath);
        // Keep the generated fixture available in test output for visual inspection of the actual Razor SVG.
        var directory = Path.Combine(AppContext.BaseDirectory, "action-map-preview", type);
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "report.html");
        var pdfPath = Path.Combine(directory, "report.pdf");
        var imagePath = Path.Combine(directory, "report.png");
        await File.WriteAllTextAsync(input, "<!doctype html><html><head><meta charset='utf-8'><style>" + css +
            "body{font:14px Arial;margin:0;background:white}.no-print{display:none!important}@page{size:A4 landscape;margin:11mm 12mm 19mm}</style></head><body>" + html + "</body></html>");
        var start = new ProcessStartInfo(ChromeTheoryAttribute.ChromePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "--headless", "--disable-gpu", "--no-pdf-header-footer", "--no-first-run",
            "--disable-extensions", "--disable-background-networking", "--user-data-dir=" + Path.Combine(directory, "profile"),
            "--window-size=1200,1000", "--screenshot=" + imagePath, "--print-to-pdf=" + pdfPath, new Uri(input).AbsoluteUri })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.Equal(0, process.ExitCode);
        var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdfPath));
        // One work-summary page followed by one complete map/legend page; diagnostics add none.
        Assert.Equal(2, Regex.Matches(pdf, @"/Type\s*/Page\b").Count);
        Assert.True(new FileInfo(imagePath).Length > 1000);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Action_maps_render_before_photos_and_shared_reports_have_no_name_editor(bool banana)
    {
        // Embedded maps need neither extra API calls nor a logged-in identity in a public report.
        var maps = SurveyActionMapTests.Fixture(banana ? "banana" : "multicrop");
        var html = banana
            ? await Render<BananaReport>(new BananaOnlineReportDto { ActionMaps = maps, Photos = [new() { Url = "https://example.test/photo.jpg" }] })
            : await Render<MultiCropReport>(new MultiCropOnlineReportDto { ActionMaps = maps, Photos = [new() { Url = "https://example.test/photo.jpg" }] });
        Assert.True(html.IndexOf("action-map-section", StringComparison.Ordinal) < html.IndexOf(banana ? "id=\"banana-photos-title\"" : "id=\"photos-title\"", StringComparison.Ordinal));
        Assert.Contains("map-canvas", html);
        Assert.Contains("Apply fertiliser", html);
        Assert.DoesNotContain("Edit map names", html);
        Assert.DoesNotContain("maps-print-hidden", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reports_without_actions_omit_maps_and_reports_without_kml_still_render(bool banana)
    {
        var empty = banana ? await Render<BananaReport>(new BananaOnlineReportDto()) : await Render<MultiCropReport>(new MultiCropOnlineReportDto());
        Assert.DoesNotContain("action-map-section", empty);
        var maps = SurveyActionMapTests.Fixture(banana ? "banana" : "multicrop");
        maps.Blocks.Clear();
        var html = banana ? await Render<BananaReport>(new BananaOnlineReportDto { ActionMaps = maps })
            : await Render<MultiCropReport>(new MultiCropOnlineReportDto { ActionMaps = maps });
        Assert.Contains("No block boundaries", html);
        Assert.DoesNotContain("map-canvas", html);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reports_render_account_or_share_creator_brand(bool banana, bool shared)
    {
        // Shared preferences take precedence over the viewer's account default.
        var parameters = new Dictionary<string, object?>
        {
            ["Report"] = banana ? new BananaOnlineReportDto() : new MultiCropOnlineReportDto(),
            ["Shared"] = shared,
            ["SharedBrandKey"] = shared ? ReportBranding.TotalGrowerServicesKey : null
        };
        var preferences = new TestReportPreferences(shared ? ReportBranding.MetagenKey : ReportBranding.TotalGrowerServicesKey);
        var html = banana
            ? await RenderParameters<BananaReport>(parameters, preferences)
            : await RenderParameters<MultiCropReport>(parameters, preferences);
        Assert.Contains("/images/branding/total-grower-services.png", html);
        Assert.Contains("TGS Pty Ltd", html);
        Assert.Equal(shared ? 0 : 1, preferences.ReadCount);
    }

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
    public async Task Banana_print_places_checklist_after_actions_and_omits_duplicate_work_summary()
    {
        var report = new BananaOnlineReportDto
        {
            Comments = [new() { Title = "Notes", Text = "Read this first" }],
            Actions = [new() { ActionGroupId = 1, Recommendation = "Feed", Blocks = "A" }],
            ActionMaps = SurveyActionMapTests.Fixture(),
            Charts = [new() { Code = "mites-beneficials", Title = "Mites" }, new() { Code = "sigatoka-lesions", Title = "Lesions", IsHistorical = true }, new() { Code = "leaf-emergence-rate", Title = "LER", IsHistorical = true }],
            RustThrips = new() { Dates = [new DateTime(2026, 9, 3)], Rows = [new() { BlockName = "A", Values = ["P"] }] },
            Tables = [new() { Code = "nematode", Title = "Nematode" }, new() { Code = "soil-health", Title = "Soil health" }, new() { Code = "bwb", Title = "BWB" }, new() { Code = "pest-summary", Title = "Pest summary", Rows = [new() { Label = "A" }] }]
        };
        var html = await Render<BananaReport>(report);
        // Keep the map and its key while omitting the second list of recommendations.
        Assert.DoesNotContain("Work summary", html);
        Assert.Contains("map-canvas", html);
        // The worker checklist is included in the report markup for print only.
        Assert.Matches("<section class=\"report-panel operational-section action-checklist-section print-only\" aria-labelledby=\"banana-action-checklist-title\"", html);
        Assert.Contains("checklist-target", html);
        Ordered(html, "banana-actions-title", "banana-action-checklist-title", "banana-chart-leaf-emergence-rate", "banana-chart-sigatoka-lesions", "banana-chart-mites-beneficials", "rust-thrips-history-title", "banana-table-pest-summary", "banana-table-bwb", "banana-table-soil-health", "banana-table-nematode");
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
        Assert.Equal(1, await CountPrintedPages(html, temporaryRoot));
    }

    [ChromeTheory]
    [InlineData(13)]
    [InlineData(20)]
    public async Task Rust_thrips_history_prints_33_block_rows_on_one_page(int visitCount)
    {
        // Match the reported block count and exercise both the actual and widest date layouts.
        var dates = Enumerable.Range(0, visitCount).Select(index => new DateTime(2026, 11, 16).AddDays(-28 * index)).ToList();
        var report = new BananaOnlineReportDto
        {
            RustThrips = new()
            {
                Dates = dates,
                Rows = Enumerable.Range(1, 33).Select(index => new BananaRustThripsRowDto
                {
                    BlockName = $"Block {index} North",
                    Values = dates.Select(_ => "P").ToList()
                }).ToList()
            }
        };

        var reportHtml = await Render<BananaReport>(report);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MetaGrow.Web"))) root = root.Parent;
        Assert.NotNull(root);
        var css = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src", "MetaGrow.Web", "obj", "Debug", "net10.0",
            "scopedcss", "bundle", "MetaGrow.Web.styles.css"));
        var html = "<!doctype html><html><head><style>body{margin:0;font-family:Arial,sans-serif}" +
            css + "</style></head><body>" + reportHtml + "</body></html>";

        // The cover is one page and the complete rust thrips table should fit on the next.
        Assert.Equal(2, await CountPrintedPages(html, Path.GetFullPath(Path.GetTempPath())));
    }

    [ChromeTheory]
    [InlineData(true, "none", 1)]
    [InlineData(true, "best-fit", 3)]
    [InlineData(true, "1", 12)]
    [InlineData(true, "2", 4)]
    [InlineData(true, "3", 3)]
    [InlineData(false, "none", 1)]
    [InlineData(false, "best-fit", 3)]
    [InlineData(false, "1", 12)]
    [InlineData(false, "2", 4)]
    [InlineData(false, "3", 3)]
    public async Task Photo_layouts_paginate_mixed_orientations_with_captions(bool banana, string layout, int expectedPages)
    {
        // Eleven photos span multiple sheets in every layout and include long wrapping comments.
        var photos = Enumerable.Range(0, 11).Select(index => new
        {
            Url = "data:image/svg+xml," + Uri.EscapeDataString(
                $"<svg xmlns='http://www.w3.org/2000/svg' width='{(index % 2 == 0 ? 400 : 600)}' height='{(index % 2 == 0 ? 600 : 400)}'><rect width='100%' height='100%' fill='green'/></svg>"),
            Comments = "A complete field observation with enough words to wrap under a photograph in the three column layout."
        }).ToList();
        var reportHtml = banana
            ? await Render<BananaReport>(new BananaOnlineReportDto
            {
                Photos = photos.Select(photo => new BananaReportPhotoDto { Url = photo.Url, Comments = photo.Comments }).ToList()
            })
            : await Render<MultiCropReport>(new MultiCropOnlineReportDto
            {
                Photos = photos.Select(photo => new MultiCropReportPhotoDto { Url = photo.Url, Comments = photo.Comments }).ToList()
            });
        // Static rendering has no browser cookie. Apply the same selection that the interactive binding supplies.
        if (layout != ReportPhotoPrinting._none)
            reportHtml = reportHtml.Replace("survey-print-report ", "survey-print-report include-print-photos ", StringComparison.Ordinal);
        reportHtml = reportHtml.Replace(ReportPhotoPrinting.PrintStyle("none", 11),
            ReportPhotoPrinting.PrintStyle(layout, 11), StringComparison.Ordinal);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MetaGrow.Web"))) root = root.Parent;
        Assert.NotNull(root);
        var css = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src", "MetaGrow.Web", "obj", "Debug", "net10.0",
            "scopedcss", "bundle", "MetaGrow.Web.styles.css"));
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src", "MetaGrow.Web", "wwwroot", "scripts", "report-printing.js"));
        var html = "<!doctype html><html><head><style>body{margin:0;font-family:Arial,sans-serif}" + css +
            "</style></head><body>" + reportHtml + "<script>" + script +
            "window.metaGrowReportPrinting._fitPhotoRows();</script></body></html>";
        Assert.Equal(expectedPages, await CountPrintedPages(html, Path.GetFullPath(Path.GetTempPath())));
    }

    private static async Task<int> CountPrintedPages(string html, string temporaryRoot)
    {
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
            return Regex.Matches(pdf, @"/Type\s*/Page\b").Count;
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

    [Fact]
    public async Task Compact_history_chart_keeps_end_dates_when_visits_cluster_near_the_end()
    {
        var dates = new[] { new DateTime(2025, 12, 2), new DateTime(2026, 10, 20), new DateTime(2026, 11, 16) };
        var section = new MultiCropReportSectionDto
        {
            Title = "History",
            IsHistorical = true,
            Series = [new() { Name = "This farm", Points = dates.Select(date => new MultiCropReportPointDto { Date = date, Value = 1 }).ToList() }]
        };

        var html = await RenderParameters<MultiCropPrintChart>(new() { ["Section"] = section, ["Compact"] = true });

        // The labels stay within the existing view box while every observation remains plotted.
        Assert.Contains("viewBox=\"0 0 1000 840\"", html);
        Assert.Matches("<text class=\"axis-label\" x=\"56\" [^>]+>02 Dec 25</text>", html);
        Assert.Matches("<text class=\"axis-label\" x=\"944\" [^>]+>16 Nov 26</text>", html);
        Assert.DoesNotContain(">20 Oct 26</text>", html);
        Assert.Equal(dates.Length, Regex.Matches(html, "class=\"history-point\"").Count);
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

    private static async Task<string> RenderParameters<T>(Dictionary<string, object?> parameters, IReportPreferenceApiClient? preferences = null) where T : IComponent
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton(DispatchProxy.Create<ITgsApiService, EmptyProxy>());
        collection.AddSingleton<IJSRuntime, NoJavaScript>();
        collection.AddSingleton(preferences ?? DispatchProxy.Create<IReportPreferenceApiClient, EmptyProxy>());
        // The unopened sharing dialog does not issue API requests during report rendering.
        collection.AddSingleton(new ReportShareApiClient(null!, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportShareApiClient>.Instance));
        collection.AddSingleton<NavigationManager, TestNavigation>();
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
    private sealed class TestReportPreferences(string _brand) : IReportPreferenceApiClient
    {
        public int ReadCount { get; private set; }
        public Task<string> GetAsync() { ReadCount++; return Task.FromResult(_brand); }
        public Task SaveAsync(string brandKey) => throw new NotSupportedException();
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
