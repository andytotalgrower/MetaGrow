using System.Reflection;
using System.Text.Json;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Metagen.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Api.Tests;

public sealed class SoilReportTests
{
    [Theory]
    [InlineData(0, false, false, 14)]
    [InlineData(30, false, false, 22.4)]
    [InlineData(40, false, false, 28)]
    [InlineData(10, false, false, 71)]
    [InlineData(20, false, false, 0)]
    [InlineData(0, true, false, 0)]
    [InlineData(0, false, true, .14)]
    public void Required_amounts_match_legacy_formula_order(int formula, bool inverse, bool percent, double expected)
    {
        var source = Source(); var target = source.Targets[0];
        target.FormulaEnum = formula; target.BelowTargetIsGood = inverse; target.IsPercent = percent; target.Params = formula == 10 ? "2" : null;
        var report = Calculate(source);
        Assert.Equal(expected, report.Blocks[0].Requirements.Single().Amount, 8);
        Assert.Equal(5.1, report.Blocks[0].Values["CecMeq"].Value!.Value, 8);
        Assert.Equal(5.1, report.Blocks[0].Values["Tec"].Value!.Value, 8);
        Assert.Equal(2, report.Blocks[0].Values["OrganicCarbon"].Value!.Value, 8);
        Assert.Contains(report.Trace, step => step.Rule == $"required-formula-{formula}" && step.Inputs["actual"] == 5);
    }

    [Fact]
    public void Full_soil_set_uses_import_reference_normalization_and_survey_crop_independently_of_other_materials()
    {
        var source = Source(); source.Survey.Samples[0].LabRefNo = "000123"; source.Samples[0].SampleReference = "123";
        source.Survey.Samples[0].CropTypeName = "Banana"; source.Samples[0].LabCrop = "Sugarcane";
        source.Survey.Samples.Add(new() { ItemId = 22, TestTypeId = 999, LabStatusId = 0, CropTypeName = "Potato", BlockId = 1 });
        var report = Calculate(source);
        Assert.Equal("Banana", report.Blocks.Single().Crop);
        Assert.Contains(report.Notes, note => note.RuleId == "crop-banana");
        Assert.DoesNotContain(report.Notes, note => note.RuleId is "crop-sugar" or "crop-potato");
        source.Samples.Add(Clone(source.Samples[0])); source.Samples[1].LabSoilId++;
        Assert.Throws<InvalidOperationException>(() => Calculate(source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(99)]
    [InlineData(100)]
    public void Four_imported_results_allow_a_report_regardless_of_the_legacy_lab_status(int labStatus)
    {
        var source = Source();
        var originalSample = source.Survey.Samples[0];
        var originalResult = source.Samples[0];
        source.Survey.Samples.Clear(); source.Samples.Clear();
        // Reproduce legacy surveys such as 7208: four returned results, separate lab references/barcodes,
        // and survey workflow statuses that were never advanced by the old import process.
        for (var index = 0; index < 4; index++)
        {
            var sample = Clone(originalSample);
            sample.ItemId += index; sample.BarcodeId += index; sample.BlockId += index;
            sample.BlockName = $"Block {index + 1}"; sample.LabRefNo = $"13071584{index}";
            sample.LabStatusId = labStatus;
            var result = Clone(originalResult);
            result.LabSoilId += index; result.BlockId = sample.BlockId; result.SampleReference = sample.LabRefNo;
            source.Survey.Samples.Add(sample); source.Samples.Insert(0, result);
        }

        var report = Calculate(source);

        Assert.Equal(4, report.Blocks.Count);
        Assert.Equal(4, report.Blocks.Select(block => block.LabSoilId).Distinct().Count());
        Assert.All(report.Blocks, block => Assert.Equal(28, block.Recommendations.Single().Rate));
        Assert.All(source.Survey.Samples, sample => Assert.Equal(labStatus, sample.LabStatusId));
    }

    [Fact]
    public void Completed_workflow_status_does_not_replace_a_missing_or_unmatched_result()
    {
        var source = Source();
        var missing = Clone(source.Survey.Samples[0]);
        missing.ItemId++; missing.BarcodeId++; missing.LabRefNo = "124"; missing.BlockId = 2; missing.BlockName = "Missing block";
        source.Survey.Samples.Add(missing);
        var error = Assert.Throws<InvalidOperationException>(() => Calculate(source));
        Assert.Contains("2 expected soil samples but only 1 matched results", error.Message);
        Assert.Contains("Missing block (item 8, barcode 124)", error.Message);

        // Equal counts alone are insufficient: the second result must identify the second survey sample.
        var unmatched = Clone(source.Samples[0]); unmatched.LabSoilId++; unmatched.SampleReference = "999"; unmatched.BlockId = 999;
        source.Samples.Add(unmatched);
        error = Assert.Throws<InvalidOperationException>(() => Calculate(source));
        Assert.Contains("Review the soil sample matches", error.Message);
    }

    [Fact]
    public void Product_rounding_limits_missing_values_and_notes_are_explicit()
    {
        var source = Source(); source.Products[0].RoundTo = 10; source.Products[0].RoundType = "U";
        source.ProductLimits.Add(new() { TemplateId = 1, RegionId = 1, ProductName = "Sulphur", MinAmount = 0, MaxAmount = 25 });
        source.Samples[0].ImportWarning = "Laboratory crop differs from the survey.";
        var report = Calculate(source);
        Assert.Equal(25, report.Blocks[0].Recommendations.Single().Rate);
        Assert.Contains(report.Notes, note => note.Text == source.Samples[0].ImportWarning);
        Assert.Contains(report.Notes, note => note.RuleId == "disclaimer");
        Assert.Contains(report.Notes, note => note.RuleId == "boron-application-area");
        source.Samples[0].Values["SulphurPpm"] = null;
        report = Calculate(source);
        Assert.Null(report.Blocks[0].Values["SulphurPpm"].Value);
        Assert.Contains(report.Warnings, warning => warning.Contains("legacy zero default"));
        Assert.Contains(report.Trace, step => step.Rule == "missing-lab-value");
    }

    [Theory]
    [InlineData(1, false, "low", "deficient")]
    [InlineData(20, false, "high", "caution")]
    [InlineData(20, true, "high", "deficient")]
    [InlineData(1, true, "low", "caution")]
    public void Priority_colours_preserve_actual_position(double actual, bool inverse, string position, string severity)
    {
        var value = new SoilReportValue { Value = actual, Minimum = 5, Maximum = 10, BelowTargetIsGood = inverse };
        Assert.Equal(position, value.Position); Assert.Equal(severity, value.Severity);
    }

    [Fact]
    public void Grandfathered_blank_TEC_parameter_retains_legacy_zero_with_a_visible_QA_warning()
    {
        var source = Source(); source.Targets[0].FormulaEnum = 10; source.Targets[0].Params = null;
        var report = Calculate(source);
        Assert.Equal(0, report.Blocks[0].Requirements.Single().Amount);
        Assert.Empty(report.Blocks[0].Recommendations);
        Assert.Contains(report.Warnings, warning => warning.Contains("missing or invalid parameters"));
        Assert.Contains(report.Trace, step => step.Rule == "invalid-legacy-formula-parameters");
    }

    [Theory]
    [InlineData("CopperPpm", 2, 0)]
    [InlineData("ZincPpm", .5, 18)]
    public void Copper_and_zinc_use_minimum_as_the_application_trigger(string field, double actual, double expectedRate)
    {
        var source = Source(); var target = source.Targets[0];
        target.ObjectName = field; target.AcceptableRangeMin = 1; target.Target = 5;
        source.Samples[0].Values[field] = actual;
        var report = Calculate(source);
        Assert.Equal(expectedRate, report.Blocks[0].Recommendations.Sum(item => item.Rate));
        if (actual >= 1) Assert.Contains(report.Trace, step => step.Rule == "copper-zinc-minimum");
    }

    [Fact]
    public void Cation_boron_tokens_use_the_original_minimum_before_updating_the_range()
    {
        var source = Source(); var target = source.Targets[0]; var template = source.Template.Settings;
        target.ObjectName = "BoronPpm"; target.AcceptableRangeMin = 1; source.Samples[0].Values["BoronPpm"] = .7;
        template.IsCationBoron = true; template.CatRule1Val1 = "6";
        template.CatRule1Target = "[MINORACTUAL]"; template.CatRule1Min = "[ACTUAL]"; template.CatRule1Max = "[MINORACTUAL]";
        var report = Calculate(source); var value = report.Blocks[0].Values["BoronPpm"];
        // This surprising ordering is intentional legacy parity, rather than normalizing the final range.
        Assert.Equal(1, value.Target); Assert.Equal(.7, value.Minimum); Assert.Equal(.7, value.Maximum);
        Assert.Equal(.6, report.Blocks[0].Requirements.Single().Amount, 8);
        Assert.Contains(report.Trace, step => step.Rule == "cation-boron");
    }

    [Fact]
    public void History_uses_all_blocks_unweighted_and_a_reproducible_three_year_window()
    {
        var source = Source(); var old = Clone(source.Samples[0]);
        old.LabSoilId = 500; old.SampleSurveyId = 99; old.SurveyDate = source.Survey.SurveyDate.AddYears(-2); old.Values["SulphurPpm"] = 10;
        var second = Clone(old); second.LabSoilId++; second.BlockId = 2; second.Values["SulphurPpm"] = 30;
        var tooOld = Clone(old); tooOld.LabSoilId += 2; tooOld.SampleSurveyId = 98; tooOld.SurveyDate = source.Survey.SurveyDate.AddYears(-3).AddDays(-1);
        source.History = [old, second, tooOld];
        var report = Calculate(source);
        Assert.Equal(2, report.History.Count);
        Assert.Equal(20, report.History.Single(round => round.SurveyId == 99).Averages["SulphurPpm"]);
        Assert.Equal(2, report.History.Single(round => round.SurveyId == 99).BlockCount);
        Assert.Single(report.Trace, step => step.LabSoilId == 100 && step.Rule == "tec");
    }

    [Fact]
    public void Fingerprint_ignores_unrelated_staff_and_billing_changes_but_tracks_calculation_inputs()
    {
        var source = Source(); var original = Calculate(source).InputHash;
        source.Survey.Agronomists.Add(new() { StaffId = 999, Email = "new@example.test" }); source.Survey.NotesForAccountant = "Billing only";
        Assert.Equal(original, Calculate(source).InputHash);
        source.Survey.Samples[0].CropTypeName = "Potato";
        Assert.NotEqual(original, Calculate(source).InputHash);
    }

    [Fact]
    public void Report_keeps_the_assigned_agronomists_contact_details_and_detects_contact_corrections()
    {
        var source = Source();
        var assigned = source.Survey.Agronomists.Single(person => person.StaffId == source.Survey.AgronomistId);
        var original = Calculate(source);
        assigned.MobilePhone = "0400 123 456";
        var corrected = Calculate(source);
        Assert.Equal(assigned.Email, corrected.AgronomistEmail);
        Assert.Equal(assigned.MobilePhone, corrected.AgronomistPhone);
        // Editable previews must not silently reuse a revision that is missing the corrected contact.
        Assert.NotEqual(original.InputHash, corrected.InputHash);
        source.Survey.Agronomists.Single(person => person.StaffId != assigned.StaffId).MobilePhone = "0400 999 999";
        Assert.Equal(corrected.InputHash, Calculate(source).InputHash);
    }

    [Fact]
    public void Removing_a_calculated_product_preserves_its_calculation_and_can_be_undone()
    {
        var source = Source();
        var document = Calculate(source);
        var originalRate = document.Blocks[0].Recommendations[0].CalculatedRate;
        var otherBlock = Clone(document.Blocks[0]); otherBlock.LabSoilId = 101;
        document.Blocks.Add(otherBlock);
        var edit = new SoilReportEdit { Actions = Clone(document.Actions), Options = Clone(document.Options),
            Overrides = [new() { LabSoilId = 100, Product = "Sulphur", Rate = 0, Reason = "No application planned for this block" }] };
        SoilReportService.ApplyEdit(document, source, edit);
        var removed = Assert.Single(document.Blocks[0].Recommendations);
        Assert.Equal(0, removed.Rate);
        Assert.Equal(originalRate, removed.CalculatedRate);
        Assert.True(removed.Overridden);
        Assert.Equal("No application planned for this block", removed.Reason);
        Assert.Equal(originalRate, otherBlock.Recommendations[0].Rate);
        // The popup's undo action removes the override, restoring the original calculation for this block only.
        edit.Overrides.Clear();
        SoilReportService.ApplyEdit(document, source, edit);
        Assert.Equal(originalRate, document.Blocks[0].Recommendations[0].Rate);
        Assert.False(document.Blocks[0].Recommendations[0].Overridden);
        Assert.Empty(document.Blocks[0].Recommendations[0].Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Revisions_freeze_at_QA_reject_self_approval_and_preserve_overrides_and_audits(int labStatus)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options); await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
        database.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = "reviewer", Email = "reviewer@example.test", EmailConfirmed = true });
        await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        var api = DispatchProxy.Create<ITgsApiService, SoilInputs>();
        var fake = (SoilInputs)(object)api; fake.SourceData = Source();
        // Imported results also govern the QA recheck; an unchanged legacy status must not reintroduce the gate.
        fake.SourceData.Survey.Samples[0].LabStatusId = labStatus;
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false); var reviewer = new SoilReportActor("reviewer", "reviewer@example.test", false);
        await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1, RegionId = 1 }, owner, "test");
        var first = await service.Open(10, owner, "test");
        Assert.True(first.Document!.Options.ShowIndividualBlockDetails);
        Assert.False(first.CanApprove);
        var unchanged = await service.Open(10, owner, "test"); Assert.Equal(first.Document!.Id, unchanged.Document!.Id);
        // A generated report cannot skip the agronomist's editing/review and saved-preview stages.
        var directSubmit = await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = first.Document.Id,
            ExpectedVersion = first.Settings.Version, Action = "submit", Note = "Skip review", PreviewConfirmed = true }, owner, "test"));
        Assert.Contains("save and preview", directSubmit.Message);
        var edited = await service.Edit(10, new() { ExpectedRevisionId = first.Document.Id, ExpectedVersion = first.Settings.Version,
            Actions = [new() { Title = "Sulphur", Text = "Apply after irrigation." }],
            Options = new() { ShowIndividualBlockDetails = true },
            Overrides = [new() { LabSoilId = 100, Product = "Sulphur", Rate = 20, Reason = "Local application constraint" }], Reason = "Agronomist review" }, owner);
        Assert.Equal(28, edited.Document!.Blocks[0].Recommendations[0].CalculatedRate);
        Assert.Equal(20, edited.Document.Blocks[0].Recommendations[0].Rate);
        Assert.Equal("Ready for QA", edited.Document.Status);
        Assert.Equal("Ready for QA", (await service.Open(10, owner, "test")).Document!.Status);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.Edit(10, new() { ExpectedRevisionId = first.Document.Id, ExpectedVersion = first.Settings.Version }, owner));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = edited.Document.Id,
            ExpectedVersion = edited.Settings.Version, Action = "submit", Note = "Preview unchecked" }, owner, "test"));
        var submitted = await service.Transition(10, new() { ExpectedRevisionId = edited.Document.Id, ExpectedVersion = edited.Settings.Version, Action = "submit", Note = "Ready for review", PreviewConfirmed = true }, owner, "test");
        var frozen = await database.SoilReportRevisions.AsNoTracking().SingleAsync(item => item.Id == edited.Document.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version, Action = "approve", Note = "Reviewed", QaHours = .25 }, owner, "test"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version, Action = "approve", Note = "Admin self review", QaHours = .25 }, owner with { IsAdmin = true }, "test"));
        var approved = await service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version, Action = "approve", Note = "Independent check complete", QaHours = .5 }, reviewer, "test");
        Assert.Equal("Approved", approved.Document!.Status);
        Assert.True(approved.Document.Options.ShowIndividualBlockDetails);
        fake.SourceData.Samples[0].Values["SulphurPpm"] = 9;
        var reopened = await service.Open(10, owner, "test");
        Assert.Equal(approved.Document.Id, reopened.Document!.Id); Assert.Equal(20, reopened.Document.Blocks[0].Recommendations[0].Rate);
        Assert.Equal(frozen.DocumentJson, (await database.SoilReportRevisions.AsNoTracking().SingleAsync(item => item.Id == frozen.Id)).DocumentJson);
        var editing = await service.Transition(10, new() { ExpectedRevisionId = approved.Document.Id, ExpectedVersion = approved.Settings.Version, Action = "return", Note = "Review corrected lab data" }, owner, "test");
        var recalculated = await service.Open(10, owner, "test");
        Assert.NotEqual(editing.Document!.Id, recalculated.Document!.Id);
        Assert.Equal(12, recalculated.Document.Blocks[0].Recommendations[0].Rate);
        Assert.Contains(recalculated.Document.Warnings, warning => warning.Contains("Previous rate overrides"));
        Assert.Equal(.5, (await database.SoilReportAudits.SingleAsync(item => item.Action == "approve")).QaHours);
        Assert.Equal(edited.Document.Id, (await database.SoilReportAudits.SingleAsync(item => item.Action == "preview")).RevisionId);
        Assert.All(fake.Calls, call => Assert.StartsWith("Get", call));
    }

    [Fact]
    public async Task Changed_inputs_require_a_new_review_and_revision_editors_cannot_approve_their_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
        database.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = "editor", Email = "reviewer@example.test", EmailConfirmed = true },
            new ApplicationUser { Id = "peer", Email = "peer@example.test", EmailConfirmed = true });
        await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilInputs>();
        var fake = (SoilInputs)(object)api; fake.SourceData = Source();
        fake.SourceData.Survey.Agronomists.Add(new() { StaffId = 3, StaffName = "Peer", Email = "peer@example.test" });
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false);
        var editor = new SoilReportActor("editor", "reviewer@example.test", true);
        var peer = new SoilReportActor("peer", "peer@example.test", false);
        await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1 }, owner, "test");
        var opened = await service.Open(10, owner, "test");
        var reviewed = await service.Edit(10, new() { ExpectedRevisionId = opened.Document!.Id, ExpectedVersion = opened.Settings.Version,
            Reason = "Agronomist reviewed the recommendations" }, editor);
        Assert.Equal("Ready for QA", reviewed.Document!.Status);
        Assert.False(reviewed.CanApprove); // Even an administrator who is not the survey agronomist is a contributor.
        fake.SourceData.Samples[0].Values["SulphurPpm"] = 6;
        var changed = await service.Open(10, editor, "test");
        Assert.Equal("Editing", changed.Document!.Status);
        Assert.NotEqual(reviewed.Document.Id, changed.Document.Id);
        var calculated = await database.SoilReportAudits.SingleAsync(item => item.Action == "calculate" && item.RevisionId == changed.Document.Id);
        Assert.Equal("Ready for QA", calculated.PreviousStatus);
        reviewed = await service.Edit(10, new() { ExpectedRevisionId = changed.Document.Id, ExpectedVersion = changed.Settings.Version,
            Reason = "Reviewed the updated laboratory result" }, editor);
        var submitted = await service.Transition(10, new() { ExpectedRevisionId = reviewed.Document!.Id, ExpectedVersion = reviewed.Settings.Version,
            Action = "submit", Note = "Preview checked", PreviewConfirmed = true }, owner, "test");
        var selfApproval = await Assert.ThrowsAsync<ArgumentException>(() => service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id,
            ExpectedVersion = submitted.Settings.Version, Action = "approve", Note = "Checked", QaHours = .25 }, editor, "test"));
        Assert.Contains("prepared or submitted", selfApproval.Message);
        var approved = await service.Transition(10, new() { ExpectedRevisionId = submitted.Document!.Id, ExpectedVersion = submitted.Settings.Version,
            Action = "approve", Note = "Independent peer QA", QaHours = .25 }, peer, "test");
        Assert.Equal("Approved", approved.Document!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Section_only_edits_generate_audit_reasons_and_preserve_reviewed_content(bool withExistingReview)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
        await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilInputs>(); ((SoilInputs)(object)api).SourceData = Source();
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false);
        await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1 }, owner, "test");
        var opened = await service.Open(10, owner, "test");
        var initial = ReviewEdit(opened);
        initial.Options.ShowIndividualBlockDetails = false;
        initial.Reason = "Reviewed the initial report";
        if (withExistingReview)
        {
            initial.Actions = [new() { Title = "Sulphur", Text = "Apply after irrigation." }];
            initial.Overrides = [new() { LabSoilId = 100, Product = "Sulphur", Rate = 20, Reason = "Local application constraint" }];
        }
        var reviewed = await service.Edit(10, initial, owner);
        var frozen = await database.SoilReportRevisions.AsNoTracking().SingleAsync(item => item.Id == reviewed.Document!.Id);

        // Reproduce the popup: it resends existing advice/overrides with a blank note and one checkbox changed.
        var sectionEdit = ReviewEdit(reviewed);
        sectionEdit.Options.ShowIndividualBlockDetails = true;
        sectionEdit.Reason = withExistingReview ? "  " : "";
        var saved = await service.Edit(10, sectionEdit, owner);
        Assert.True(saved.Document!.Options.ShowIndividualBlockDetails);
        Assert.Equal("Ready for QA", saved.Document.Status);
        Assert.Equal(JsonSerializer.Serialize(reviewed.Document!.Actions), JsonSerializer.Serialize(saved.Document.Actions));
        Assert.Equal(JsonSerializer.Serialize(reviewed.Document.Blocks), JsonSerializer.Serialize(saved.Document.Blocks));
        var audit = await database.SoilReportAudits.SingleAsync(item => item.RevisionId == saved.Document.Id && item.Action == "edit");
        Assert.Equal("Report sections changed: Individual block details enabled.", audit.Reason);
        Assert.Equal(owner.Id, audit.ActorId);
        Assert.Equal(frozen.DocumentJson, (await database.SoilReportRevisions.AsNoTracking().SingleAsync(item => item.Id == frozen.Id)).DocumentJson);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.Edit(10, sectionEdit, owner));

        // Every report-section switch uses the same rule and produces an explicit, bounded audit entry.
        var hideSections = ReviewEdit(saved);
        hideSections.Options = new() { ShowStatusOverview = false, ShowBlockDetails = false, ShowRequiredAmounts = false,
            ShowHistory = false, ShowSugarInputs = false, ShowIndividualBlockDetails = false };
        var hidden = await service.Edit(10, hideSections, owner);
        var hiddenAudit = await database.SoilReportAudits.SingleAsync(item => item.RevisionId == hidden.Document!.Id && item.Action == "edit");
        Assert.Equal("Report sections changed: Status overview disabled; Block results disabled; Theoretical amounts disabled; Historical Farm Averages disabled; Sugarcane inputs disabled; Individual block details disabled.", hiddenAudit.Reason);

        var namedReview = ReviewEdit(hidden);
        namedReview.Options.ShowIndividualBlockDetails = true;
        namedReview.Reason = "  Show the grower each block's results  ";
        var named = await service.Edit(10, namedReview, owner);
        Assert.Equal(namedReview.Reason.Trim(), (await database.SoilReportAudits.SingleAsync(item => item.RevisionId == named.Document!.Id && item.Action == "edit")).Reason);
        Assert.All(((SoilInputs)(object)api).Calls, call => Assert.StartsWith("Get", call));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("rate")]
    [InlineData("restore rate")]
    [InlineData("adjustment reason")]
    [InlineData("excluded block")]
    [InlineData("no changes")]
    [InlineData("long note")]
    public async Task Section_toggle_cannot_bypass_review_note_validation(string change)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
        await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilInputs>();
        var source = Source(); ((SoilInputs)(object)api).SourceData = source;
        // A second matched sample makes the exclusion valid, isolating the review-note requirement.
        var sample = Clone(source.Survey.Samples[0]);
        sample.ItemId = 8; sample.BarcodeId = 124; sample.LabRefNo = "124"; sample.BlockId = 2; sample.BlockName = "South";
        source.Survey.Samples.Add(sample);
        source.Survey.Blocks.Add(new() { BlockId = 2, BlockName = "South", Size = 9 });
        var labSample = Clone(source.Samples[0]);
        labSample.LabSoilId = 101; labSample.BlockId = 2; labSample.BlockName = "South"; labSample.SampleReference = "124";
        source.Samples.Add(labSample);
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var owner = new SoilReportActor("owner", "owner@example.test", false);
        await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1 }, owner, "test");
        var opened = await service.Open(10, owner, "test");
        var initial = ReviewEdit(opened);
        initial.Options.ShowIndividualBlockDetails = false;
        initial.Overrides = [new() { LabSoilId = 100, Product = "Sulphur", Rate = 20, Reason = "Local application constraint" }];
        initial.Reason = "Initial agronomist review";
        var reviewed = await service.Edit(10, initial, owner);
        var edit = ReviewEdit(reviewed);
        edit.Options.ShowIndividualBlockDetails = true;
        switch (change)
        {
            case "action": edit.Actions.Add(new() { Title = "New advice", Text = "Apply after irrigation." }); break;
            case "rate": edit.Overrides[0].Rate = 21; break;
            case "restore rate": edit.Overrides.Clear(); break;
            case "adjustment reason": edit.Overrides[0].Reason = "A different application constraint"; break;
            case "excluded block": edit.ExcludedLabSoilIds.Add(101); break;
            case "no changes": edit.Options.ShowIndividualBlockDetails = false; break;
            case "long note": edit.Reason = new string('x', 1001); break;
        }
        var revisionCount = await database.SoilReportRevisions.CountAsync();
        var auditCount = await database.SoilReportAudits.CountAsync();
        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.Edit(10, edit, owner));
        Assert.Contains("1–1000", error.Message);
        Assert.Equal(revisionCount, await database.SoilReportRevisions.CountAsync());
        Assert.Equal(auditCount, await database.SoilReportAudits.CountAsync());
        var state = await database.SoilReportStates.AsNoTracking().SingleAsync();
        Assert.Equal(reviewed.Settings.Version, state.Version);
        Assert.Equal(reviewed.Document!.Id, state.CurrentRevisionId);
    }

    // Mirror the popup's complete payload so existing recommendations are not accidentally removed in a test.
    private static SoilReportEdit ReviewEdit(SoilReportWorkspace workspace) => new()
    {
        ExpectedRevisionId = workspace.Document!.Id, ExpectedVersion = workspace.Settings.Version,
        Actions = Clone(workspace.Document.Actions), Options = Clone(workspace.Document.Options),
        ExcludedLabSoilIds = workspace.Document.Blocks.Where(block => block.Excluded).Select(block => block.LabSoilId).ToList(),
        Overrides = workspace.Document.Blocks.SelectMany(block => block.Recommendations.Where(item => item.Overridden)
            .Select(item => new SoilReportOverride { LabSoilId = block.LabSoilId, Product = item.Product, Rate = item.Rate, Reason = item.Reason })).ToList()
    };

    [Fact]
    public async Task Failed_audit_insert_rolls_back_revision_and_workflow_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options); await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
        database.SoilReportStates.Add(new() { SurveyId = 10, TemplateId = 1, RegionId = 1, Version = 1 });
        await database.SaveChangesAsync(); database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_soil_audit BEFORE INSERT ON SoilReportAudits BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;");
        var api = DispatchProxy.Create<ITgsApiService, SoilInputs>(); ((SoilInputs)(object)api).SourceData = Source();
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.Open(10, new("owner", "owner@example.test", false), "test"));
        await using var check = new ApplicationDbContext(options);
        Assert.Empty(await check.SoilReportRevisions.ToListAsync());
        var state = await check.SoilReportStates.SingleAsync(); Assert.Null(state.CurrentRevisionId); Assert.Equal(1, state.Version);
    }

    internal static SoilReportSource Source() => new()
    {
        Survey = new() { SurveyId = 10, PropertyId = 12, PropertyName = "Test farm", SurveyDate = new(2026, 9, 15), AgronomistId = 1, AgronomistName = "Owner", CreatedBy = "owner@example.test",
            LabTestTypes = [new() { LabTestTypeId = 11, LabId = 1, TestTypeId = 10 }],
            Samples = [new() { ItemId = 7, SurveyId = 10, BarcodeId = 123, LabRefNo = "123", LabId = 1, TestTypeId = 11, LabStatusId = 100, BlockId = 1, BlockName = "North", CropTypeName = "Avocado" }],
            Agronomists = [new() { StaffId = 1, StaffName = "Owner", Email = "owner@example.test" }, new() { StaffId = 2, StaffName = "Reviewer", Email = "reviewer@example.test" }], Blocks = [new() { BlockId = 1, BlockName = "North", Size = 9 }] },
        Template = new() { Settings = new() { TemplateId = 1, TemplateName = "Soil cropping", TestTypeId = 10, CropId = 1 } },
        Targets = [new() { TargetId = 1, ItemId = 1, ItemName = "Sulphur ppm", ObjectName = "SulphurPpm", AcceptableRangeMin = 10, AcceptableRangeMax = 20, Target = 12, BalanceProductId = 1, BalanceProductName = "Sulphur", BalanceParams = "2" }],
        Products = [new() { SoilTestProductId = 1, ProductName = "Sulphur", DisplayOrder = 1 }],
        Samples = [new() { LabSoilId = 100, SampleSurveyId = 10, LabId = 1, LabTestTypeId = 11, BlockId = 1, BlockName = "North", SampleReference = "123", LabName = "Test laboratory", SurveyDate = new(2026, 9, 15),
            Values = new(StringComparer.OrdinalIgnoreCase) { ["PhH2o"] = 7, ["PhCaCl2"] = 6.2, ["CalciumMeq"] = 4, ["MagnesiumMeq"] = .8, ["PotassiumMeq"] = .2, ["SodiumMeq"] = .1, ["SulphurPpm"] = 5, ["OrganicMatterPc"] = 3.44 } }]
    };
    private static SoilReportDocument Calculate(SoilReportSource source) => new SoilReportCalculator(new(SoilReportDefinitionTests.SeedDefinitions())).Calculate(source, 1);
    private static T Clone<T>(T item) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item))!;

    public class SoilInputs : DispatchProxy
    {
        public SoilReportSource SourceData { get; set; } = new();
        public List<string> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            return method.Name switch
            {
                nameof(ITgsApiService.GetSoilReportSource) => Task.FromResult<SoilReportSource?>(Clone(SourceData)),
                nameof(ITgsApiService.GetSampleSurveyEditor) => Task.FromResult<SampleSurveyEditorDto?>(Clone(SourceData.Survey)),
                nameof(ITgsApiService.GetActiveTgsStaff) => Task.FromResult<List<Staff>?>(Clone(SourceData.Survey.Agronomists)),
                nameof(ITgsApiService.GetSoilReportOptions) => Task.FromResult<SoilReportSourceOptions?>(new() { Templates = [SourceData.Template.Settings],
                    TissueTemplates = SourceData.Tissue.Templates.Select(template => template.Settings).ToList(), Regions = [new() { Id = 1, Name = "Default" }] }),
                _ => throw new NotSupportedException("Unexpected Tgs API call: " + method.Name)
            };
        }
    }
}
