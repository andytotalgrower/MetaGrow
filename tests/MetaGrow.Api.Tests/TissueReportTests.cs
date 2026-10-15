using System.Text.Json;
using System.Reflection;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Metagen.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Api.Tests;

public sealed class TissueReportTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Material_only_review_is_audited_persisted_and_frozen_for_peer_QA(bool soil, bool tissue)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions().Append(Definition()));
        database.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = "reviewer", Email = "reviewer@example.test", EmailConfirmed = true });
        await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilReportTests.SoilInputs>();
        var fake = (SoilReportTests.SoilInputs)(object)api; fake.SourceData = Source();
        // Template crop comes from the catalogue by ID; names and sample crops deliberately disagree.
        fake.SourceData.Template.Settings.CropId = 2;
        fake.SourceData.Template.Settings.TemplateName = "Generic calculation template";
        fake.SourceData.Survey.Crops = [new() { CropTypeId = 2, CropTypeName = "Sugar" }, new() { CropTypeId = 55, CropTypeName = "Avocado" }];
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false);
        var peer = new SoilReportActor("reviewer", "reviewer@example.test", false);
        await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1, TissueTemplates = [new() { CropId = 55, TemplateId = 200 }] }, owner, "test");
        var opened = await service.Open(10, owner, "test");
        Assert.True(opened.IsSugarcaneTemplate); Assert.Equal("Sugar", opened.TemplateCropName);
        var review = Review(opened);
        review.Actions = [new() { Title = "Retained advice", Text = "Review application timing." }];
        review.Overrides = [new() { LabSoilId = 100, Product = "Sulphur", Rate = 20, Reason = "Application constraint" }];
        review.Reason = "Reviewed the advice and application rates.";
        var prepared = await service.Edit(10, review, owner);
        var originalJson = (await database.SoilReportRevisions.AsNoTracking().SingleAsync(row => row.Id == prepared.Document!.Id)).DocumentJson;
        var toggle = Review(prepared); toggle.Options.IncludeSoil = soil; toggle.Options.IncludeTissue = tissue;
        var saved = await service.Edit(10, toggle, owner);
        Assert.Equal(soil, saved.Document!.Options.IncludeSoil); Assert.Equal(tissue, saved.Document.Options.IncludeTissue);
        Assert.Equal(JsonSerializer.Serialize(prepared.Document!.Blocks), JsonSerializer.Serialize(saved.Document.Blocks));
        Assert.Equal("Retained advice", Assert.Single(saved.Document.Actions).Title);
        var audit = await database.SoilReportAudits.SingleAsync(row => row.RevisionId == saved.Document.Id && row.Action == "edit");
        Assert.Contains(soil ? "Tissue disabled" : "Soil disabled", audit.Reason);
        Assert.Equal(originalJson, (await database.SoilReportRevisions.AsNoTracking().SingleAsync(row => row.Id == prepared.Document.Id)).DocumentJson);
        var reopened = await service.Open(10, owner, "test");
        Assert.Equal(saved.Document.Id, reopened.Document!.Id); Assert.Equal(soil, reopened.Document.Options.IncludeSoil);
        var submitted = await service.Transition(10, new() { ExpectedRevisionId = saved.Document.Id, ExpectedVersion = saved.Settings.Version,
            Action = "submit", PreviewConfirmed = true, Note = "Preview checked." }, owner, "test");
        var approved = await service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version,
            Action = "approve", QaHours = .25, Note = "Peer checked." }, peer, "test");
        Assert.Equal(soil, approved.Document!.Options.IncludeSoil); Assert.Equal(tissue, approved.Document.Options.IncludeTissue);
        // Restoring both materials changes presentation while keeping the same reviewed advice/rates.
        var restored = Clone(approved.Document); var restore = Review(approved);
        restore.Options.IncludeSoil = true; restore.Options.IncludeTissue = true;
        SoilReportService.ApplyEdit(restored, fake.SourceData, restore);
        Assert.Equal("Soil And Tissue Report", restored.Title);
        Assert.Equal(20, restored.Blocks[0].Recommendations.Single(item => item.Product == "Sulphur").Rate);
        Assert.All(fake.Calls, call => Assert.StartsWith("Get", call));
    }

    [Fact]
    public void Material_validation_rejects_empty_output_and_tissue_without_an_included_sample()
    {
        var source = Source(); var report = Calculate(source);
        var error = Assert.Throws<ArgumentException>(() => SoilReportService.ApplyEdit(report, source,
            new() { Options = new() { IncludeSoil = false, IncludeTissue = false } }));
        Assert.Contains("at least one", error.Message);
        var excluded = Assert.Throws<ArgumentException>(() => SoilReportService.ApplyEdit(report, source,
            new() { Options = new() { IncludeSoil = false }, ExcludedLabSoilIds = [100] }));
        Assert.Contains("tissue results", excluded.Message);
        report.Tissue.Samples.Clear();
        Assert.Throws<ArgumentException>(() => SoilReportService.ApplyEdit(report, source, new() { Options = new() { IncludeSoil = false } }));
        Assert.True(report.Options.IncludeSoil); // Invalid requests do not alter the current document.
    }

    [Theory]
    [InlineData("Sugar", true)]
    [InlineData(" sugarcane ", true)]
    [InlineData("Banana", false)]
    [InlineData("", false)]
    public void Sugarcane_section_availability_uses_the_selected_template_crop(string crop, bool expected)
    {
        var workspace = new SoilReportWorkspace { TemplateCropName = crop,
            Document = new() { TemplateName = "Sugarcane template name is not evidence", Blocks = [new() { Crop = "Sugar" }] } };
        Assert.Equal(expected, workspace.IsSugarcaneTemplate);
    }

    private static SoilReportEdit Review(SoilReportWorkspace workspace) => new()
    {
        ExpectedRevisionId = workspace.Document!.Id, ExpectedVersion = workspace.Settings.Version,
        Options = Clone(workspace.Document.Options), Actions = Clone(workspace.Document.Actions),
        ExcludedLabSoilIds = workspace.Document.Blocks.Where(block => block.Excluded).Select(block => block.LabSoilId).ToList(),
        Overrides = workspace.Document.Blocks.SelectMany(block => block.Recommendations.Where(item => item.Overridden)
            .Select(item => new SoilReportOverride { LabSoilId = block.LabSoilId, Product = item.Product, Rate = item.Rate, Reason = item.Reason })).ToList()
    };

    [Fact]
    public async Task Template_popup_settings_audits_and_QA_freeze_cover_the_same_combined_revision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions().Append(Definition()));
        database.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = "reviewer", Email = "reviewer@example.test", EmailConfirmed = true });
        await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilReportTests.SoilInputs>();
        var fake = (SoilReportTests.SoilInputs)(object)api; fake.SourceData = Source();
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false);
        var peer = new SoilReportActor("reviewer", "reviewer@example.test", false);
        var settings = await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1 }, owner, "test");
        Assert.False(settings.TemplatesComplete); Assert.Equal(55, Assert.Single(settings.TissueCrops).CropId);
        var preparation = await service.Open(10, owner, "test");
        Assert.True(preparation.TemplateSelectionRequired); Assert.Null(preparation.Document);
        settings.TissueTemplates = [new() { CropId = 55, TemplateId = 200 }];
        settings = await service.SaveSettings(settings, owner, "test"); Assert.True(settings.TemplatesComplete);
        Assert.Single(await database.SoilReportAudits.Where(audit => audit.Action == "tissue-templates").ToListAsync());
        // Missing requested tissue is previewable, but a complete combined set is required for peer QA.
        var tissue = fake.SourceData.Tissue.Samples[0]; fake.SourceData.Tissue.Samples.Clear();
        var pending = await service.Open(10, owner, "test");
        Assert.Single(pending.Document!.Tissue.MissingSamples);
        var pendingReview = await service.Edit(10, new() { ExpectedRevisionId = pending.Document.Id, ExpectedVersion = pending.Settings.Version,
            Reason = "Previewed the soil results while tissue is pending." }, owner);
        var incomplete = await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = pendingReview.Document!.Id,
            ExpectedVersion = pendingReview.Settings.Version, Action = "submit", PreviewConfirmed = true, Note = "Preview checked." }, owner, "test"));
        Assert.Contains("Tissue results are still missing", incomplete.Message);
        // Pending tissue does not block a deliberately soil-only revision from entering peer QA.
        var soilOnly = Review(pendingReview); soilOnly.Options.IncludeTissue = false;
        var soilSaved = await service.Edit(10, soilOnly, owner);
        var soilSubmitted = await service.Transition(10, new() { ExpectedRevisionId = soilSaved.Document!.Id,
            ExpectedVersion = soilSaved.Settings.Version, Action = "submit", PreviewConfirmed = true, Note = "Soil preview checked." }, owner, "test");
        Assert.Equal("Awaiting QA", soilSubmitted.Document!.Status);
        await service.Transition(10, new() { ExpectedRevisionId = soilSubmitted.Document.Id,
            ExpectedVersion = soilSubmitted.Settings.Version, Action = "return", Note = "Tissue has now arrived; prepare the combined version." }, owner, "test");
        fake.SourceData.Tissue.Samples.Add(tissue);
        var opened = await service.Open(10, owner, "test");
        var reviewed = await service.Edit(10, new() { ExpectedRevisionId = opened.Document!.Id, ExpectedVersion = opened.Settings.Version,
            Reason = "Reviewed soil advice and tissue targets." }, owner);
        var submitted = await service.Transition(10, new() { ExpectedRevisionId = reviewed.Document!.Id, ExpectedVersion = reviewed.Settings.Version,
            Action = "submit", PreviewConfirmed = true, Note = "Preview checked." }, owner, "test");
        fake.SourceData.Tissue.Templates[0].Targets[0].Target = 3.2;
        var changed = await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id,
            ExpectedVersion = submitted.Settings.Version, Action = "approve", QaHours = .25, Note = "Peer checked." }, peer, "test"));
        Assert.Contains("changed", changed.Message);
        fake.SourceData.Tissue.Templates[0].Targets[0].Target = 3;
        var approved = await service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version,
            Action = "approve", QaHours = .25, Note = "Peer checked." }, peer, "test");
        fake.SourceData.Tissue.Samples[0].Values["NitrogenPc"] = 9;
        var frozen = await service.Open(10, owner, "test");
        Assert.Equal(approved.Document!.Id, frozen.Document!.Id);
        Assert.Equal(2, frozen.Document.Tissue.Samples[0].Values["NitrogenPc"].Value);
        Assert.All(fake.Calls, call => Assert.StartsWith("Get", call));
    }

    [Fact]
    public void Independent_template_names_and_survey_crop_supply_tissue_ranges_without_application_calculations()
    {
        var source = Source();
        var soilOnly = Clone(source); soilOnly.Survey.Samples.RemoveAll(sample => sample.TestTypeId == 21); soilOnly.Tissue = new();
        var report = Calculate(source);
        var sample = Assert.Single(report.Tissue.Samples);
        Assert.Equal("Avocado", sample.Crop); // Lab crop and nominal template crop are deliberately wrong.
        Assert.Equal("Independent tissue targets", sample.TemplateName);
        Assert.Equal("low", sample.Values["NitrogenPc"].Position);
        Assert.Equal(3, sample.Values["NitrogenPc"].Target);
        Assert.Equal(JsonSerializer.Serialize(Calculate(soilOnly).Blocks[0].Recommendations), JsonSerializer.Serialize(report.Blocks[0].Recommendations));
        Assert.Equal("Soil And Tissue Report", report.Title);
        Assert.Equal(100, Assert.Single(sample.SoilLabIds));
    }

    [Fact]
    public void Tissue_only_blocks_do_not_appear_or_invalidate_the_combined_report()
    {
        var source = Source(); var baseline = Calculate(source);
        var otherItem = Clone(source.Survey.Samples[1]); otherItem.ItemId = 9; otherItem.BlockId = 2; otherItem.BlockName = "Tissue only"; otherItem.LabRefNo = "999";
        source.Survey.Samples.Add(otherItem);
        var other = Clone(source.Tissue.Samples[0]); other.LabTissueId = 201; other.BlockId = 2; other.BlockName = "Tissue only"; other.SampleReference = "999";
        source.Tissue.Samples.Add(other);
        var report = Calculate(source);
        Assert.Single(report.Tissue.Samples);
        Assert.Equal(baseline.InputHash, report.InputHash);
        Assert.Empty(report.Tissue.MissingSamples);
    }

    [Fact]
    public void Missing_readings_remain_blank_and_real_zero_remains_a_reading()
    {
        var source = Source(); source.Tissue.Samples[0].Values["NitrogenPc"] = null; source.Tissue.Samples[0].Values["BoronPpm"] = 0;
        var report = Calculate(source);
        Assert.Null(report.Tissue.Samples[0].Values["NitrogenPc"].Value);
        Assert.Equal(0, report.Tissue.Samples[0].Values["BoronPpm"].Value);
        Assert.DoesNotContain(report.Tissue.Measures, measure => measure.Key == "NitrogenPc");
        Assert.Contains(report.Tissue.Measures, measure => measure.Key == "BoronPpm");
    }

    [Fact]
    public void Crop_block_mapping_resolves_a_lab_alias_without_requiring_matching_references()
    {
        var source = Source(); var lab = source.Tissue.Samples[0];
        lab.BlockId = 0; lab.BlockName = "LAB NORTH"; lab.SampleReference = "LAB-NEW-99";
        source.Tissue.BlockMappings.Add(new() { LabBlockName = "LAB NORTH", BlockId = 1, BlockType = "mcs" });
        var report = Calculate(source);
        Assert.Equal("North", report.Tissue.Samples[0].BlockName);
        Assert.Equal("mapped-block", report.Tissue.Matches[0].Method);
    }

    [Fact]
    public void Banana_and_crop_block_ID_collisions_require_review()
    {
        var source = Source(); source.Tissue.Samples[0].BlockType = "banana";
        var scope = TissueReportAssembler.Prepare(source, [], []);
        Assert.True(TissueReportAssembler.Review(scope, []).RequiresReview);
    }

    [Fact]
    public void Multiple_tissue_samples_keep_their_parts_and_values_separate()
    {
        var source = Source(); var item = Clone(source.Survey.Samples[1]); item.ItemId = 9; item.LabRefNo = "T-2";
        source.Survey.Samples.Add(item);
        var lab = Clone(source.Tissue.Samples[0]); lab.LabTissueId = 201; lab.SampleReference = "T-2"; lab.SamplePart = "petiole"; lab.Values["NitrogenPc"] = 4;
        source.Tissue.Samples.Add(lab);
        var report = Calculate(source);
        Assert.Equal(2, report.Tissue.Samples.Count);
        Assert.Contains(report.Tissue.Samples, sample => sample.SamplePart == "leaf" && sample.Values["NitrogenPc"].Value == 2);
        Assert.Contains(report.Tissue.Samples, sample => sample.SamplePart == "petiole" && sample.Values["NitrogenPc"].Value == 4);
    }

    [Fact]
    public void Missing_requested_tissue_is_visible_in_preview_without_becoming_zero()
    {
        var source = Source(); source.Tissue.Samples.Clear();
        var report = Calculate(source);
        Assert.Empty(report.Tissue.Samples);
        Assert.Single(report.Tissue.MissingSamples);
        Assert.Contains(report.Warnings, message => message.Contains("Tissue results are pending"));
    }

    [Fact]
    public void A_changed_tissue_result_or_target_invalidates_the_QA_fingerprint()
    {
        var source = Source(); var baseline = Calculate(source);
        source.Tissue.Samples[0].Values["NitrogenPc"] = 2.5;
        var changed = Calculate(source); Assert.NotEqual(baseline.InputHash, changed.InputHash);
        source.Tissue.Templates[0].Targets[0].Target = 3.2;
        Assert.NotEqual(changed.InputHash, Calculate(source).InputHash);
        Assert.Equal(3, baseline.Tissue.Samples[0].Values["NitrogenPc"].Target);
    }

    [Fact]
    public void Tissue_requires_an_explicit_active_template_and_rejects_duplicate_items()
    {
        var source = Source(); source.Tissue.Selections.Clear();
        Assert.Contains("Choose a tissue template", Assert.Throws<InvalidOperationException>(() => Calculate(source)).Message);
        source.Tissue.Selections.Add(new() { CropId = 55, TemplateId = 200 });
        source.Tissue.Templates[0].Targets.Add(Clone(source.Tissue.Templates[0].Targets[0]));
        Assert.Contains("duplicate test items", Assert.Throws<InvalidOperationException>(() => Calculate(source)).Message);
    }

    [Fact]
    public void Old_core_deployments_cannot_silently_omit_tissue()
    {
        var source = Source(); source.Tissue.Loaded = false;
        Assert.Contains("Deploy the combined-report API", Assert.Throws<InvalidOperationException>(() => Calculate(source)).Message);
    }

    [Fact]
    public void Mixed_crop_surveys_use_each_crops_selected_template()
    {
        var source = Source();
        var soil = Clone(source.Survey.Samples[0]); soil.ItemId = 10; soil.BlockId = 2; soil.BlockName = "South"; soil.CropTypeId = 56; soil.CropTypeName = "Banana"; soil.LabRefNo = "456"; soil.BarcodeId = 456;
        var tissue = Clone(source.Survey.Samples[1]); tissue.ItemId = 11; tissue.BlockId = 2; tissue.BlockName = "South"; tissue.CropTypeId = 56; tissue.CropTypeName = "Banana"; tissue.LabRefNo = "T-3";
        source.Survey.Samples.AddRange([soil, tissue]);
        source.Survey.Blocks.Add(new() { BlockId = 2, BlockName = "South", BlockType = "mcs" });
        var soilLab = Clone(source.Samples[0]); soilLab.LabSoilId = 101; soilLab.BlockId = 2; soilLab.BlockName = "South"; soilLab.SampleReference = "456";
        source.Samples.Add(soilLab);
        var lab = Clone(source.Tissue.Samples[0]); lab.LabTissueId = 201; lab.BlockId = 2; lab.BlockName = "South"; lab.SampleReference = "T-3"; source.Tissue.Samples.Add(lab);
        var template = Clone(source.Tissue.Templates[0]); template.Settings.TemplateId = 201; template.Settings.TemplateName = "Banana foliar"; template.Targets[0].Target = 3.5;
        source.Tissue.Templates.Add(template); source.Tissue.Selections.Add(new() { CropId = 56, TemplateId = 201 });
        var report = Calculate(source);
        Assert.Equal(3, report.Tissue.Samples.Single(sample => sample.Crop == "Avocado").Values["NitrogenPc"].Target);
        Assert.Equal(3.5, report.Tissue.Samples.Single(sample => sample.Crop == "Banana").Values["NitrogenPc"].Target);
    }

    internal static SoilReportSource Source()
    {
        var source = SoilReportTests.Source(); source.Survey.Samples[0].CropTypeId = 55; source.Survey.Blocks[0].BlockType = "mcs";
        source.Survey.LabTestTypes.Add(new() { LabTestTypeId = 21, LabId = 1, TestTypeId = 20 });
        source.Survey.Samples.Add(new() { ItemId = 8, SurveyId = 10, BlockId = 1, BlockName = "North", CropTypeId = 55, CropTypeName = "Avocado", TestTypeId = 21, LabId = 1, LabRefNo = "T-1" });
        source.Tissue = new()
        {
            Loaded = true, Selections = [new() { CropId = 55, TemplateId = 200 }],
            Samples = [new() { LabTissueId = 200, SampleSurveyId = 10, BlockId = 1, BlockType = "mcs", BlockName = "North", LabId = 1, LabTestTypeId = 21,
                LabCrop = "Incorrect lab crop", SampleReference = "T-1", SamplePart = "leaf", SurveyDate = source.Survey.SurveyDate,
                Values = new() { ["NitrogenPc"] = 2, ["BoronPpm"] = 40, ["CopperPpm"] = null } }],
            Templates = [new() { Settings = new() { TemplateId = 200, TemplateName = "Independent tissue targets", TestTypeId = 20, CropId = 999 },
                Targets = [new() { TargetId = 2, ItemId = 2, ObjectName = "NitrogenPc", ItemName = "Nitrogen %", AcceptableRangeMin = 2.5, Target = 3, AcceptableRangeMax = 4 }] }]
        };
        return source;
    }
    internal static SoilReportDefinition Definition() => new()
    {
        Key = TissueReportAssembler._definitionKey, Name = "Tissue", Sections = [new() { Id = 1, Name = "Nutrients", Items =
            [new() { Id = 1, Field = "NitrogenPc", Label = "Nitrogen %" }, new() { Id = 2, Field = "BoronPpm", Label = "Boron ppm" }, new() { Id = 3, Field = "CopperPpm", Label = "Copper ppm" }] }]
    };
    private static SoilReportDocument Calculate(SoilReportSource source) => new SoilReportCalculator(new(SoilReportDefinitionTests.SeedDefinitions().Append(Definition()))).Calculate(source, 1);
    private static T Clone<T>(T item) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item))!;
}
