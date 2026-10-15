using System.Reflection;
using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Metagen.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Api.Tests;

public sealed class SoilReportMatchingTests
{
    [Theory]
    [InlineData("05715844")]
    [InlineData("")]
    public void A_unique_mapped_block_does_not_require_a_barcode_or_matching_reference(string reference)
    {
        var source = SoilReportTests.Source();
        source.Survey.Samples[0].LabRefNo = reference; source.Survey.Samples[0].BarcodeId = 0;
        source.Samples[0].SampleReference = "130464090";
        source.Samples[0].BlockName = "NORTH OLD LAB LABEL";
        var report = Calculate(source);
        Assert.Equal("mapped-block", Assert.Single(report.SampleMatches).Method);
        Assert.Equal("North", Assert.Single(report.Blocks).Name);
        Assert.Contains(report.Trace, step => step.Rule == "sample-match-mapped-block" && step.Result == 7);
        Assert.Equal(reference, source.Survey.Samples[0].LabRefNo);
        Assert.Equal(0, source.Survey.Samples[0].BarcodeId);
    }

    [Fact]
    public void Typed_names_ignore_case_and_whitespace_but_do_not_guess_similar_names()
    {
        var source = SoilReportTests.Source();
        source.Survey.Samples[0].BlockId = 0; source.Survey.Samples[0].BlockName = " North  Field ";
        source.Survey.Samples[0].LabRefNo = ""; source.Survey.Samples[0].BarcodeId = 0;
        source.Samples[0].BlockName = "NORTH\tFIELD";
        Assert.Equal("block-name", Assert.Single(Calculate(source).SampleMatches).Method);
        source.Samples[0].BlockName = "North-Field";
        Assert.True(SoilReportSampleMatcher.Review(source).RequiresReview);
    }

    [Fact]
    public void Block_and_reference_conflicts_require_confirmation()
    {
        var source = TwoSamples();
        source.Samples[0].SampleReference = source.Survey.Samples[1].LabRefNo!;
        var review = SoilReportSampleMatcher.Review(source);
        Assert.True(review.RequiresReview);
        Assert.Contains("identify different", review.Rows.Single(row => row.LabSoilId == 100).Message);
        Assert.Throws<InvalidOperationException>(() => Calculate(source));
    }

    [Fact]
    public void References_disambiguate_multiple_soil_samples_on_one_block_independently_of_order()
    {
        var source = TwoSamples();
        source.Survey.Samples[1].BlockId = 1; source.Samples[1].BlockId = 1;
        source.Samples.Reverse();
        var matches = SoilReportSampleMatcher.Match(source);
        Assert.Equal(7, matches[100].ItemId); Assert.Equal(8, matches[101].ItemId);
        Assert.All(Calculate(source).SampleMatches, match => Assert.Equal("mapped-block-reference", match.Method));
        source.Samples[1].SampleReference = "";
        Assert.True(SoilReportSampleMatcher.Review(source).RequiresReview);
    }

    [Fact]
    public void Duplicate_results_cannot_silently_reuse_a_survey_sample()
    {
        var source = SoilReportTests.Source();
        var duplicate = Copy(source.Samples[0]); duplicate.LabSoilId++;
        source.Samples.Add(duplicate);
        var review = SoilReportSampleMatcher.Review(source);
        Assert.All(review.Rows, row => Assert.True(row.RequiresReview));
        Assert.Throws<InvalidOperationException>(() => Calculate(source));
    }

    [Fact]
    public void Same_names_or_references_cannot_cross_survey_lab_material_or_conflicting_block_ids()
    {
        var source = SoilReportTests.Source();
        source.Samples[0].SampleSurveyId++;
        Assert.Empty(Assert.Single(SoilReportSampleMatcher.Review(source).Rows).Choices);
        source = SoilReportTests.Source(); source.Samples[0].LabId++;
        Assert.Empty(Assert.Single(SoilReportSampleMatcher.Review(source).Rows).Choices);
        source = SoilReportTests.Source(); source.Survey.LabTestTypes.Add(new() { LabTestTypeId = 99, LabId = 1, TestTypeId = 20 });
        source.Samples[0].LabTestTypeId = 99;
        Assert.Empty(Assert.Single(SoilReportSampleMatcher.Review(source).Rows).Choices);
        source = SoilReportTests.Source(); source.Samples[0].BlockId = 999;
        Assert.True(SoilReportSampleMatcher.Review(source).RequiresReview);
        Assert.Contains("saved block link differs", Assert.Single(SoilReportSampleMatcher.Review(source).Rows).Message);
    }

    [Fact]
    public void Banana_and_multicrop_block_id_collisions_are_not_the_same_block()
    {
        var source = SoilReportTests.Source();
        source.Survey.Blocks[0].BlockType = "banana";
        source.Survey.Blocks.Add(new() { BlockId = 1, BlockName = "Other block", BlockType = "mcs" });
        source.Samples[0].Metadata["BlockType"] = JsonSerializer.SerializeToElement("mcs");
        Assert.True(SoilReportSampleMatcher.Review(source).RequiresReview);
        source.Samples[0].Metadata["BlockType"] = JsonSerializer.SerializeToElement("banana");
        Assert.False(SoilReportSampleMatcher.Review(source).RequiresReview);
    }

    [Fact]
    public void Matched_or_ambiguous_results_cannot_be_hidden_as_not_sampled()
    {
        var source = TwoSamples();
        source.Survey.Samples[0].LabRefNo = "outdated"; source.Survey.Samples[0].BarcodeId = 0;
        Assert.True(SoilReportSampleMatcher.HasLabResult(source, source.Survey.Samples[0]));
        Assert.Throws<ArgumentException>(() => SoilReportSampleMatcher.ValidateNotSampled(source,
            [new() { SurveyItemId = 7, Reason = "Should not hide a mapped result" }]));
        source.Survey.Samples[1].BlockId = 1; source.Samples[0].SampleReference = "missing";
        Assert.True(SoilReportSampleMatcher.HasLabResult(source, source.Survey.Samples[1]));
    }

    [Fact]
    public void Manual_pairings_are_frozen_and_changed_identifiers_require_reconfirmation()
    {
        var source = TwoSamples();
        var manual = Confirm(source, 100, 8);
        var other = Confirm(source, 101, 7);
        var automatic = Calculate(source);
        var changed = Calculate(source, [manual, other]);
        Assert.NotEqual(automatic.InputHash, changed.InputHash);
        Assert.Equal(8, changed.SampleMatches.Single(match => match.LabSoilId == 100).SurveyItemId);
        source.Samples[0].SampleReference = "corrected reference";
        Assert.True(SoilReportSampleMatcher.Review(source, [manual, other]).RequiresReview);
        Assert.Throws<InvalidOperationException>(() => Calculate(source, [manual, other]));
    }

    [Fact]
    public async Task Manual_matching_is_audited_versioned_and_invalidates_preview_without_writing_to_totalgs()
    {
        await using var fixture = await Fixture.Create();
        var source = fixture.Inputs.SourceData;
        source.Samples[0].SampleReference = "124"; // Block and reference point to different samples.
        var opened = await fixture.Service.Open(10, fixture.Actor, "test");
        Assert.Null(opened.Document); Assert.True(opened.Matching!.RequiresReview);
        Assert.Empty(await fixture.Database.SoilReportRevisions.ToListAsync());
        var change = new SoilReportMatchingChange { ExpectedVersion = opened.Settings.Version, ExpectedInputHash = opened.Matching.InputHash,
            Items = [new() { LabSoilId = 100, SurveyItemId = 7, Reason = "Confirmed from the mapped block", ConfirmedById = "forged" }] };
        await fixture.Service.SaveMatching(10, change, fixture.Actor, "test");
        var saved = await fixture.Service.Open(10, fixture.Actor, "test");
        Assert.Equal(fixture.Actor.Id, saved.Document!.SampleMatches.Single(match => match.LabSoilId == 100).ConfirmedById);
        Assert.Contains(await fixture.Database.SoilReportAudits.ToListAsync(), audit => audit.Action == "sample-match" && audit.ActorId == fixture.Actor.Id);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        var before = await fixture.Service.Matching(10, "test");
        source.Survey.Samples[0].LabRefNo = "changed while popup open";
        change.ExpectedVersion = before.Version; change.ExpectedInputHash = before.InputHash;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        Assert.All(fixture.Inputs.Calls, call => Assert.StartsWith("Get", call));
    }

    [Fact]
    public async Task Manual_match_validation_and_audit_failure_leave_stored_choices_unchanged()
    {
        await using var fixture = await Fixture.Create();
        var review = await fixture.Service.Matching(10, "test");
        var change = new SoilReportMatchingChange { ExpectedVersion = review.Version, ExpectedInputHash = review.InputHash,
            Items = [new() { LabSoilId = 100, SurveyItemId = 7, Reason = "" }] };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        change.Items[0].Reason = "Confirmed"; change.Items[0].SurveyItemId = 999;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        change.Items[0].SurveyItemId = 7;
        change.Items.Add(new() { LabSoilId = 101, SurveyItemId = 7, Reason = "Duplicate" });
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        change.Items.RemoveAt(1);
        await fixture.Database.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_match_audit BEFORE INSERT ON SoilReportAudits BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.SaveMatching(10, change, fixture.Actor, "test"));
        fixture.Database.ChangeTracker.Clear();
        var state = await fixture.Database.SoilReportStates.SingleAsync();
        Assert.Equal("[]", state.SampleMatchesJson); Assert.Equal(review.Version, state.Version);
    }

    [Fact]
    public async Task A_manual_match_confirmer_cannot_approve_the_resulting_revision_and_QA_freezes_choices()
    {
        await using var fixture = await Fixture.Create();
        var matcher = new SoilReportActor("reviewer", "reviewer@example.test", false);
        var review = await fixture.Service.Matching(10, "test");
        await fixture.Service.SaveMatching(10, new() { ExpectedVersion = review.Version, ExpectedInputHash = review.InputHash,
            Items = [new() { LabSoilId = 100, SurveyItemId = 7, Reason = "Checked field records" }] }, matcher, "test");
        var opened = await fixture.Service.Open(10, fixture.Actor, "test");
        var preview = await fixture.Service.Edit(10, new() { ExpectedRevisionId = opened.Document!.Id, ExpectedVersion = opened.Settings.Version,
            Reason = "Agronomist review complete" }, fixture.Actor);
        var submitted = await fixture.Service.Transition(10, new() { ExpectedRevisionId = preview.Document!.Id, ExpectedVersion = preview.Settings.Version,
            Action = "submit", PreviewConfirmed = true, Note = "Preview checked" }, fixture.Actor, "test");
        var approval = await fixture.Service.Open(10, matcher, "test");
        Assert.False(approval.CanApprove); Assert.Contains("confirmed its sample matches", approval.ApprovalMessage);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveMatching(10,
            new() { ExpectedVersion = submitted.Settings.Version }, matcher, "test"));
    }

    private static SoilReportDocument Calculate(SoilReportSource source, List<SoilReportManualMatch>? manual = null) =>
        new SoilReportCalculator(new(SoilReportDefinitionTests.SeedDefinitions())).Calculate(source, 1, manualMatches: manual);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static SoilReportManualMatch Confirm(SoilReportSource source, int resultId, int itemId) => new()
    {
        LabSoilId = resultId, SurveyItemId = itemId, Reason = "Field records checked", ConfirmedById = "reviewer",
        SourceHash = SoilReportSampleMatcher.PairHash(source, source.Samples.Single(result => result.LabSoilId == resultId), source.Survey.Samples.Single(sample => sample.ItemId == itemId))
    };
    private static SoilReportSource TwoSamples()
    {
        var source = SoilReportTests.Source();
        var sample = Copy(source.Survey.Samples[0]); sample.ItemId = 8; sample.BlockId = 2; sample.BlockName = "South"; sample.BarcodeId = 124; sample.LabRefNo = "124";
        source.Survey.Samples.Add(sample);
        source.Survey.Blocks.Add(new() { BlockId = 2, BlockName = "South", Size = 9 });
        var result = Copy(source.Samples[0]); result.LabSoilId = 101; result.BlockId = 2; result.BlockName = "South"; result.SampleReference = "124";
        source.Samples.Add(result); return source;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApplicationDbContext Database { get; private set; } = null!;
        public SoilReportService Service { get; private set; } = null!;
        public SoilReportTests.SoilInputs Inputs { get; private set; } = null!;
        public SoilReportActor Actor { get; } = new("owner", "owner@example.test", false);
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture(); await fixture._connection.OpenAsync();
            fixture.Database = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(fixture._connection).Options);
            await fixture.Database.Database.EnsureCreatedAsync();
            fixture.Database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions());
            fixture.Database.Users.AddRange(new ApplicationUser { Id = "owner", Email = "owner@example.test", EmailConfirmed = true },
                new ApplicationUser { Id = "reviewer", Email = "reviewer@example.test", EmailConfirmed = true });
            await fixture.Database.SaveChangesAsync();
            var api = DispatchProxy.Create<ITgsApiService, SoilReportTests.SoilInputs>();
            fixture.Inputs = (SoilReportTests.SoilInputs)(object)api; fixture.Inputs.SourceData = TwoSamples();
            fixture.Service = new(fixture.Database, api, new(fixture.Database), NullLogger<SoilReportService>.Instance);
            await fixture.Service.SaveSettings(new() { SurveyId = 10, TemplateId = 1 }, fixture.Actor, "test");
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Database.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
