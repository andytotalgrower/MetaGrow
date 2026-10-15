using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Services.SoilReports;

public sealed partial class SoilReportService
{
    public async Task<SoilReportMatchingReview> Matching(int surveyId, string token, bool tissue = false)
    {
        var state = await State(surveyId);
        var source = await Source(state, token);
        return tissue ? TissueMatchingReview(state, source) : MatchingReview(state, source);
    }

    private static SoilReportMatchingReview MatchingReview(SoilReportState state, SoilReportSource source)
    {
        var review = SoilReportSampleMatcher.Review(source, Deserialize<List<SoilReportManualMatch>>(state.SampleMatchesJson),
            Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson));
        review.Version = state.Version;
        review.CanEdit = state.Status is "Editing" or "Ready for QA";
        return review;
    }

    public async Task<SoilReportSettings> SaveMatching(int surveyId, SoilReportMatchingChange change, SoilReportActor actor, string token, bool tissue = false)
    {
        var state = await State(surveyId);
        if (state.Version != change.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (state.Status is not ("Editing" or "Ready for QA"))
            throw new ArgumentException("Return the report to editing before changing its sample matches.");
        var source = await Source(state, token);
        var tissueScope = tissue ? TissueScope(state, source) : null;
        var review = tissue ? TissueMatchingReview(state, source) : MatchingReview(state, source);
        if (tissueScope is not null) source = tissueScope.MatchingSource;
        if (review.InputHash != change.ExpectedInputHash)
            throw new ArgumentException("The survey or lab identifiers changed while this window was open. Reload sample matches and check them again.");
        if (change.Items.Select(item => item.LabSoilId).Distinct().Count() != change.Items.Count
            || change.Items.Select(item => item.SurveyItemId).Distinct().Count() != change.Items.Count)
            throw new ArgumentException("Each lab result and survey sample can be matched only once.");
        var previous = Deserialize<List<SoilReportManualMatch>>(tissue ? state.TissueMatchesJson : state.SampleMatchesJson);
        var items = new List<SoilReportManualMatch>();
        foreach (var item in change.Items.OrderBy(item => item.LabSoilId))
        {
            var row = review.Rows.SingleOrDefault(row => row.LabSoilId == item.LabSoilId);
            if (row is null || !row.Choices.Any(choice => choice.SurveyItemId == item.SurveyItemId))
                throw new ArgumentException("Choose a collected sample of the same material, survey and laboratory for each result.");
            if (string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 500)
                throw new ArgumentException("Enter a reason of 1–500 characters for each manual match.");
            var hash = SoilReportSampleMatcher.PairHash(source, source.Samples.Single(result => result.LabSoilId == item.LabSoilId),
                source.Survey.Samples.Single(sample => sample.ItemId == item.SurveyItemId));
            // Preserve the original confirmer for unchanged choices; clients cannot forge audit attribution.
            var old = previous.SingleOrDefault(old => old.LabSoilId == item.LabSoilId && old.SurveyItemId == item.SurveyItemId
                && old.SourceHash == hash && old.Reason == item.Reason.Trim());
            items.Add(new() { LabSoilId = item.LabSoilId, SurveyItemId = item.SurveyItemId, Reason = item.Reason.Trim(), SourceHash = hash,
                ConfirmedById = old?.ConfirmedById ?? actor.Id, ConfirmedBy = old?.ConfirmedBy ?? actor.Email });
        }
        var omissions = tissue ? [] : Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson);
        var checkedReview = tissueScope is not null ? TissueReportAssembler.Review(tissueScope, items) : SoilReportSampleMatcher.Review(source, items, omissions);
        if (checkedReview.RequiresReview)
            throw new ArgumentException("Some sample matches are still unresolved. Each laboratory result must identify a different survey sample.");
        SoilReportSampleMatcher.ValidateNotSampled(source, omissions, items);
        var json = JsonSerializer.Serialize(items);
        if (json == (tissue ? state.TissueMatchesJson : state.SampleMatchesJson)) return ToSettings(state);
        var previousStatus = state.Status;
        state.Status = "Editing"; state.Version++;
        if (tissue) state.TissueMatchesJson = json; else state.SampleMatchesJson = json;
        foreach (var old in previous.Where(old => !items.Any(item => JsonSerializer.Serialize(item) == JsonSerializer.Serialize(old))))
            AddAudit(state, actor, tissue ? "tissue-match-removed" : "sample-match-removed", $"Lab result {old.LabSoilId}: removed manual match to survey item {old.SurveyItemId}. Previous reason: {old.Reason}", previousStatus);
        foreach (var item in items.Where(item => !previous.Any(old => JsonSerializer.Serialize(item) == JsonSerializer.Serialize(old))))
            AddAudit(state, actor, tissue ? "tissue-match" : "sample-match", $"Lab result {item.LabSoilId} matched to survey item {item.SurveyItemId}. {item.Reason}", previousStatus);
        // One SaveChanges transaction commits the choices, concurrency version and every audit entry together.
        await _database.SaveChangesAsync();
        _logger.LogInformation("Soil report {SurveyId} sample matches saved by {Actor}; {Count} manual choices", surveyId, actor.Email, items.Count);
        return ToSettings(state);
    }
}
