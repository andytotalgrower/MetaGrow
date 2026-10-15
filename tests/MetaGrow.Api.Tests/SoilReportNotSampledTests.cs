using System.Reflection;
using System.Text.Json;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Metagen.Shared.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Api.Tests;

public sealed class SoilReportNotSampledTests
{
    [Fact]
    public void Explicit_exception_keeps_source_unchanged_and_is_invalidated_by_a_later_lab_result()
    {
        var source = SourceWithUncollectedSample();
        var calculator = new SoilReportCalculator(new(SoilReportDefinitionTests.SeedDefinitions()));
        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(source, 1));
        var exceptions = new List<SoilReportNotSampled> { new() { SurveyItemId = 8, Reason = "Block not collected" } };
        var report = calculator.Calculate(source, 1, exceptions);
        Assert.Single(report.Blocks);
        Assert.Equal("Melon", Assert.Single(report.NotSampled).BlockName);
        Assert.Equal("Block not collected", report.NotSampled[0].Reason);
        Assert.Equal(2, source.Survey.Samples.Count);
        Assert.Single(source.Samples);
        exceptions[0].Reason = "Collection cancelled by grower";
        Assert.NotEqual(report.InputHash, calculator.Calculate(source, 1, exceptions).InputHash);

        // A later import cannot be silently omitted by an old exception.
        var laterResult = JsonSerializer.Deserialize<ApiModels.MetaGrow.SoilReportLabSample>(JsonSerializer.Serialize(source.Samples[0]))!;
        laterResult.LabSoilId = 101; laterResult.SampleReference = "124";
        source.Samples.Add(laterResult);
        Assert.Throws<ArgumentException>(() => calculator.Calculate(source, 1, exceptions));
    }

    [Theory]
    [InlineData(8, "")]
    [InlineData(7, "Already has results")]
    [InlineData(999, "Different survey")]
    public void Invalid_exclusions_are_rejected(int surveyItemId, string reason)
    {
        Assert.Throws<ArgumentException>(() => SoilReportSampleMatcher.ValidateNotSampled(SourceWithUncollectedSample(),
            [new() { SurveyItemId = surveyItemId, Reason = reason }]));
    }

    [Fact]
    public async Task Exceptions_are_audited_versioned_frozen_for_QA_and_use_default_region()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitions.AddRange(SoilReportDefinitionTests.SeedDefinitions()); await database.SaveChangesAsync();
        var api = DispatchProxy.Create<ITgsApiService, SoilReportTests.SoilInputs>();
        ((SoilReportTests.SoilInputs)(object)api).SourceData = SourceWithUncollectedSample();
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        var actor = new SoilReportActor("owner", "owner@example.test", false);
        var settings = await service.SaveSettings(new() { SurveyId = 10, TemplateId = 1, RegionId = 99 }, actor, "test");
        Assert.Equal(1, settings.RegionId);
        var change = new SoilReportNotSampledChange { ExpectedVersion = settings.Version,
            Items = [new() { SurveyItemId = 8, Reason = "Melon not sampled" }] };
        await service.SaveNotSampled(10, change, actor, "test");
        var audit = await database.SoilReportAudits.SingleAsync(item => item.Action == "not-sampled");
        Assert.Equal(actor.Email, audit.Actor);
        Assert.Contains("Melon not sampled", audit.Reason);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveNotSampled(10, change, actor, "test"));
        var opened = await service.Open(10, actor, "test");
        Assert.Single(opened.Document!.NotSampled);
        opened = await service.Edit(10, new() { ExpectedRevisionId = opened.Document.Id, ExpectedVersion = opened.Settings.Version,
            Reason = "Reviewed the included blocks and recommendations" }, actor);
        var submitted = await service.Transition(10, new() { Action = "submit", ExpectedVersion = opened.Settings.Version,
            ExpectedRevisionId = opened.Document!.Id, Note = "Checked uncollected block", PreviewConfirmed = true }, actor, "test");
        change.ExpectedVersion = submitted.Settings.Version;
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveNotSampled(10, change, actor, "test"));
        Assert.All(((SoilReportTests.SoilInputs)(object)api).Calls, call => Assert.StartsWith("Get", call));
    }

    [Fact]
    public async Task Failed_exclusion_audit_rolls_back_the_exception_and_version()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options); await database.Database.EnsureCreatedAsync();
        database.SoilReportStates.Add(new() { SurveyId = 10, TemplateId = 1, Version = 1 }); await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_soil_audit BEFORE INSERT ON SoilReportAudits BEGIN SELECT RAISE(ABORT, 'audit unavailable'); END;");
        var api = DispatchProxy.Create<ITgsApiService, SoilReportTests.SoilInputs>();
        ((SoilReportTests.SoilInputs)(object)api).SourceData = SourceWithUncollectedSample();
        var service = new SoilReportService(database, api, new(database), NullLogger<SoilReportService>.Instance);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SaveNotSampled(10, new() { ExpectedVersion = 1,
            Items = [new() { SurveyItemId = 8, Reason = "Not collected" }] }, new("owner", "owner@example.test", false), "test"));
        await using var check = new ApplicationDbContext(options);
        var state = await check.SoilReportStates.SingleAsync();
        Assert.Equal("[]", state.NotSampledJson); Assert.Equal(1, state.Version);
    }

    private static ApiModels.MetaGrow.SoilReportSource SourceWithUncollectedSample()
    {
        var source = SoilReportTests.Source();
        // Uncollected samples need an audited exception; retained samples still need matching imported results.
        source.Survey.Samples.Add(new() { ItemId = 8, SurveyId = 10, BarcodeId = 124, LabRefNo = "124",
            LabId = 1, TestTypeId = 11, LabStatusId = 0, BlockId = 2, BlockName = "Melon", CropTypeName = "Banana" });
        return source;
    }
}
