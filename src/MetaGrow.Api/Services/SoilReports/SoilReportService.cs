using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Shared.Models;
using Metagen.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Services.SoilReports;

public sealed record SoilReportActor(string Id, string Email, bool IsAdmin);

/// <summary>Owns the report lifecycle in MetaGrow. Every mutation saves its audit in the same transaction.</summary>
public sealed partial class SoilReportService(ApplicationDbContext _database, ITgsApiService _tgs,
    SoilReportDefinitionStore _definitions, ILogger<SoilReportService> _logger)
{
    public async Task<SoilReportSettings> Settings(int surveyId, string token = "")
    {
        var state = await _database.SoilReportStates.AsNoTracking().SingleOrDefaultAsync(item => item.SurveyId == surveyId)
            ?? new() { SurveyId = surveyId };
        var settings = ToSettings(state);
        // Frozen reports open without consulting newly imported tissue or requiring new template selections.
        if (state.TemplateId > 0 && token.Length > 0 && state.Status is "Editing" or "Ready for QA")
            settings.TissueCrops = TissueCrops(state, await Source(state, token));
        return settings;
    }

    public async Task<SoilReportSourceOptions> Options(string token)
    {
        var choices = await _tgs.GetSoilReportOptions(token);
        if (choices is not null) return choices;
        // Retain the downstream failure for deployment diagnosis without exposing server responses in the popup.
        _logger.LogError("Soil report choices unavailable from TgsApi.Core GET /soil-reports/options: {Detail}", _tgs.ErrorMessage);
        throw new InvalidOperationException("Soil report choices could not be loaded from TgsApi.Core. Check the API deployment and its logs.");
    }

    public async Task<SoilReportSettings> SaveSettings(SoilReportSettings settings, SoilReportActor actor, string token)
    {
        var survey = await _tgs.GetSampleSurveyEditor(settings.SurveyId) ?? throw new ArgumentException("The Sample survey could not be found.");
        var choices = await Options(token);
        // Region selection is deferred; all new calculations use the established Default region (1).
        settings.RegionId = 1;
        if (!choices.Templates.Any(item => item.TemplateId == settings.TemplateId))
            throw new ArgumentException("Choose an active soil template.");
        if (settings.TissueTemplates.Count > 100 || settings.TissueTemplates.Select(item => item.CropId).Distinct().Count() != settings.TissueTemplates.Count
            || settings.TissueTemplates.Any(item => item.CropId <= 0 || !choices.TissueTemplates.Any(template => template.TemplateId == item.TemplateId)))
            throw new ArgumentException("Choose one active tissue template for each survey crop.");
        var state = await _database.SoilReportStates.SingleOrDefaultAsync(item => item.SurveyId == survey.SurveyId);
        if (state is null)
        {
            if (settings.Version != 0) throw new DbUpdateConcurrencyException();
            state = new() { SurveyId = survey.SurveyId };
            _database.SoilReportStates.Add(state);
        }
        if (state.Version != settings.Version) throw new DbUpdateConcurrencyException();
        if (state.Status != "Editing") throw new ArgumentException("Return the report to editing before changing its calculation template.");
        var reason = $"Template {state.TemplateId} → {settings.TemplateId}; region {state.RegionId} → {settings.RegionId}.";
        state.TemplateId = settings.TemplateId; state.RegionId = settings.RegionId; state.Version++;
        var tissueJson = JsonSerializer.Serialize(settings.TissueTemplates.OrderBy(item => item.CropId));
        var previousTissue = Deserialize<List<TissueTemplateSelection>>(state.TissueTemplatesJson);
        // One bounded entry per crop keeps even a large mixed-crop survey inside the audit reason limit.
        foreach (var cropId in previousTissue.Select(item => item.CropId).Union(settings.TissueTemplates.Select(item => item.CropId)))
        {
            var before = previousTissue.SingleOrDefault(item => item.CropId == cropId)?.TemplateId ?? 0;
            var after = settings.TissueTemplates.SingleOrDefault(item => item.CropId == cropId)?.TemplateId ?? 0;
            if (before != after) AddAudit(state, actor, "tissue-templates", $"Survey crop {cropId}: tissue template {before} → {after}.");
        }
        state.TissueTemplatesJson = tissueJson;
        AddAudit(state, actor, "settings", reason);
        await _database.SaveChangesAsync();
        return await Settings(state.SurveyId, token);
    }

    public async Task<SoilReportPreparation> Preparation(int surveyId, string token)
    {
        var state = await State(surveyId);
        var source = await Source(state, token);
        return new() { Version = state.Version, CanEdit = state.Status == "Editing",
            NotSampled = Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson),
            Samples = SoilReportSampleMatcher.SoilSamples(source).OrderBy(sample => sample.BlockName)
                .Select(sample => new SoilReportSampleChoice(sample.ItemId, sample.BlockName ?? "", sample.BarcodeId,
                    SoilReportSampleMatcher.HasLabResult(source, sample, Deserialize<List<SoilReportManualMatch>>(state.SampleMatchesJson)))).ToList() };
    }

    public async Task<SoilReportSettings> SaveNotSampled(int surveyId, SoilReportNotSampledChange change, SoilReportActor actor, string token)
    {
        var state = await State(surveyId);
        if (state.Version != change.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (state.Status != "Editing") throw new ArgumentException("Return the report to editing before changing which samples were collected.");
        var source = await Source(state, token);
        SoilReportSampleMatcher.ValidateNotSampled(source, change.Items, Deserialize<List<SoilReportManualMatch>>(state.SampleMatchesJson));
        var items = change.Items.OrderBy(item => item.SurveyItemId)
            .Select(item => new SoilReportNotSampled { SurveyItemId = item.SurveyItemId, Reason = item.Reason.Trim() }).ToList();
        var json = JsonSerializer.Serialize(items);
        if (json == state.NotSampledJson) return ToSettings(state);
        var previous = Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson);
        // Capture additions, removals and reason changes individually, keeping each audit reason within its limit.
        foreach (var old in previous.Where(old => !items.Any(item => item.SurveyItemId == old.SurveyItemId && item.Reason == old.Reason)))
            AddAudit(state, actor, "not-sampled-removed", $"Survey item {old.SurveyItemId} included again. Previous exclusion reason: {old.Reason}");
        foreach (var item in items.Where(item => !previous.Any(old => item.SurveyItemId == old.SurveyItemId && item.Reason == old.Reason)))
            AddAudit(state, actor, "not-sampled", $"Survey item {item.SurveyItemId} was not collected: {item.Reason}");
        state.NotSampledJson = json;
        state.Version++;
        await _database.SaveChangesAsync();
        return ToSettings(state);
    }

    public async Task<SoilReportWorkspace> Open(int surveyId, SoilReportActor actor, string token)
    {
        var state = await State(surveyId);
        var previous = await Current(state);
        // Submitted/approved revisions stay frozen. A saved preview must be reviewed again if inputs change.
        if (state.Status is not ("Editing" or "Ready for QA") && previous is not null) return await Workspace(state, previous, actor);
        var source = await Source(state, token);
        var matching = MatchingReview(state, source);
        // Unresolved evidence is a normal preparation step, not a generic request failure.
        if (matching.RequiresReview) return new() { Settings = ToSettings(state), Matching = matching };
        var tissueMatching = TissueMatchingReview(state, source);
        if (tissueMatching.RequiresReview) return new() { Settings = ToSettings(state), TissueMatching = tissueMatching };
        var settings = ToSettings(state);
        settings.TissueCrops = TissueCrops(state, source);
        if (!settings.TemplatesComplete) return new() { Settings = settings, TemplateSelectionRequired = true };
        var (_, document) = await Calculate(state, token, source);
        if (previous is not null && previous.InputHash == document.InputHash) return await Workspace(state, previous, actor);
        if (previous is not null)
        {
            var old = Deserialize<SoilReportDocument>(previous.DocumentJson);
            document.Actions = old.Actions; document.Options = old.Options;
            // Old rate overrides must be reviewed after new lab data/rules. Keep their prior revision accessible.
            if (old.Blocks.Any(block => block.Excluded || block.Recommendations.Any(item => item.Overridden)))
                document.Warnings.Add("Inputs changed. Previous rate overrides and block exclusions remain in revision history and must be reviewed again for this calculation.");
        }
        var previousStatus = state.Status;
        state.Status = "Editing";
        var revision = AddRevision(state, source, document, actor);
        AddAudit(state, actor, "calculate", previous is null ? "Initial calculation on opening the report." : "Inputs or report definitions changed; calculated a new revision.", previousStatus);
        await _database.SaveChangesAsync();
        _logger.LogInformation("Soil report {SurveyId} revision {RevisionId} calculated using {Engine}; input hash {InputHash}", surveyId, revision.Id, document.EngineVersion, document.InputHash);
        return await Workspace(state, revision, actor);
    }

    public async Task<SoilReportWorkspace> Edit(int surveyId, SoilReportEdit edit, SoilReportActor actor)
    {
        var state = await State(surveyId);
        CheckExpected(state, edit.ExpectedRevisionId, edit.ExpectedVersion);
        if (state.Status is not ("Editing" or "Ready for QA")) throw new ArgumentException("Return the report to editing before changing recommendations.");
        var previousStatus = state.Status;
        var current = (await Current(state))!;
        var document = Deserialize<SoilReportDocument>(current.DocumentJson);
        var source = Deserialize<SoilReportSource>(current.SourceJson);
        var previousOptions = document.Options;
        var previousReviewContent = ReviewContent(document);
        ApplyEdit(document, source, edit);
        var reason = EditReason(edit.Reason, previousOptions, previousReviewContent, document);
        var revision = AddRevision(state, source, document, actor);
        // Saving the agronomist's review produces a preview; only a separate action sends it to peer QA.
        state.Status = "Ready for QA";
        AddAudit(state, actor, "edit", reason, previousStatus);
        await _database.SaveChangesAsync();
        return await Workspace(state, revision, actor);
    }

    public async Task<SoilReportWorkspace> Transition(int surveyId, SoilReportTransition request, SoilReportActor actor, string token)
    {
        var state = await State(surveyId);
        CheckExpected(state, request.ExpectedRevisionId, request.ExpectedVersion);
        var current = (await Current(state))!;
        var document = Deserialize<SoilReportDocument>(current.DocumentJson);
        var previousStatus = state.Status;
        RequireReason(request.Note);
        switch (request.Action)
        {
            case "submit" when state.Status == "Editing":
                throw new ArgumentException("Review the key actions and soil balancing recommendations, then save and preview the report before submitting it for peer QA.");
            case "submit" when state.Status == "Ready for QA":
            case "approve" when state.Status == "Awaiting QA":
                if (request.Action == "submit" && !request.PreviewConfirmed)
                    throw new ArgumentException("Check the saved report preview before submitting it for peer QA.");
                var (_, fresh) = await Calculate(state, token);
                // Missing tissue prevents QA only when tissue is part of this saved report's output.
                if (document.Options.IncludeTissue && fresh.Tissue.MissingSamples.Count > 0)
                    throw new ArgumentException("Tissue results are still missing for: " + string.Join(", ", fresh.Tissue.MissingSamples) + ". Import these results before peer QA.");
                if (fresh.InputHash != current.InputHash)
                    throw new ArgumentException("Lab results, survey details, calculation rules or definitions changed. Return to editing and reopen the report before submitting it for QA.");
                if (request.Action == "approve")
                {
                    var message = await ApprovalMessage(Deserialize<SoilReportSource>(current.SourceJson), document, actor);
                    if (message.Length > 0) throw new ArgumentException(message);
                    if (!double.IsFinite(request.QaHours) || request.QaHours <= 0 || request.QaHours > 24)
                        throw new ArgumentException("Enter QA time greater than zero and no more than 24 hours.");
                    state.Status = "Approved";
                }
                else
                {
                    AddAudit(state, actor, "preview", "Confirmed the saved report preview before submitting it for peer QA.");
                    state.Status = "Awaiting QA";
                }
                state.Version++;
                break;
            case "return" when state.Status is "Ready for QA" or "Awaiting QA" or "Approved":
                state.Status = "Editing";
                current = AddRevision(state, Deserialize<SoilReportSource>(current.SourceJson), document, actor);
                break;
            default: throw new ArgumentException("This workflow action is no longer available. Reload the report.");
        }
        AddAudit(state, actor, request.Action, request.Note, previousStatus, request.Action == "approve" ? request.QaHours : 0);
        await _database.SaveChangesAsync();
        return await Workspace(state, current, actor);
    }

    private async Task<(SoilReportSource Source, SoilReportDocument Document)> Calculate(SoilReportState state, string token, SoilReportSource? source = null)
    {
        source ??= await Source(state, token);
        var notSampled = Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson);
        var document = new SoilReportCalculator(await _definitions.SnapshotAsync()).Calculate(source, 1, notSampled,
            Deserialize<List<SoilReportManualMatch>>(state.SampleMatchesJson), Deserialize<List<SoilReportManualMatch>>(state.TissueMatchesJson));
        return (source, document);
    }

    private async Task<SoilReportSource> Source(SoilReportState state, string token)
    {
        var source = await _tgs.GetSoilReportSource(state.SurveyId, state.TemplateId, token);
        if (source is null)
        {
            // Preserve transport/conversion diagnostics internally without exposing raw source metadata to the UI.
            _logger.LogError("Soil report inputs unavailable for survey {SurveyId}, template {TemplateId}: {Detail}",
                state.SurveyId, state.TemplateId, _tgs.ErrorMessage);
            throw new InvalidOperationException("Soil report inputs could not be loaded. Check the Tgs API and its soil-report read procedures.");
        }
        if (source.Survey.SurveyId != state.SurveyId || source.Template.Settings.TemplateId != state.TemplateId)
            throw new InvalidOperationException("The returned report inputs do not match the requested survey and template.");
        source.Tissue.Selections = Deserialize<List<TissueTemplateSelection>>(state.TissueTemplatesJson);
        return source;
    }

    private async Task<SoilReportState> State(int surveyId) => await _database.SoilReportStates.SingleOrDefaultAsync(item => item.SurveyId == surveyId)
        ?? throw new ArgumentException("Select the soil calculation template on the Sample survey first.");
    private async Task<SoilReportRevision?> Current(SoilReportState state) => state.CurrentRevisionId.HasValue
        ? await _database.SoilReportRevisions.AsNoTracking().SingleAsync(item => item.Id == state.CurrentRevisionId) : null;

    private SoilReportRevision AddRevision(SoilReportState state, SoilReportSource source, SoilReportDocument document, SoilReportActor actor)
    {
        document.Id = Guid.NewGuid(); document.Revision = ++state.RevisionNumber; document.Status = "Editing";
        var revision = new SoilReportRevision { Id = document.Id, SurveyId = state.SurveyId, Number = document.Revision,
            InputHash = document.InputHash, SourceJson = JsonSerializer.Serialize(source), DocumentJson = JsonSerializer.Serialize(document),
            CreatedUtc = DateTime.UtcNow, CreatedBy = actor.Email };
        _database.SoilReportRevisions.Add(revision);
        state.CurrentRevisionId = revision.Id; state.Version++;
        return revision;
    }

    private void AddAudit(SoilReportState state, SoilReportActor actor, string action, string reason, string? previousStatus = null, double hours = 0) =>
        _database.SoilReportAudits.Add(new() { SurveyId = state.SurveyId, RevisionId = state.CurrentRevisionId,
            OccurredUtc = DateTime.UtcNow, ActorId = actor.Id, Actor = actor.Email, Action = action, Reason = reason.Trim(),
            PreviousStatus = previousStatus ?? state.Status, Status = state.Status, QaHours = hours });

    private async Task<SoilReportWorkspace> Workspace(SoilReportState state, SoilReportRevision revision, SoilReportActor actor)
    {
        var document = Deserialize<SoilReportDocument>(revision.DocumentJson); document.Status = state.Status;
        var source = Deserialize<SoilReportSource>(revision.SourceJson);
        var message = await ApprovalMessage(source, document, actor);
        return new() { Settings = ToSettings(state), Document = document, Products = source.Products,
            // The SQL header stores CropId only; resolve it against the saved crop catalogue,
            // not the crops selected on individual survey blocks or returned by the lab.
            TemplateCropName = source.Survey.Crops.FirstOrDefault(crop => crop.CropTypeId == source.Template.Settings.CropId)?.CropTypeName
                ?? source.Template.Settings.CropName,
            CanApprove = message.Length == 0, ApprovalMessage = message };
    }

    private async Task<string> ApprovalMessage(SoilReportSource source, SoilReportDocument document, SoilReportActor actor)
    {
        var addresses = await _database.UserEmailAddresses.AsNoTracking().Where(address => address.UserId == actor.Id && address.IsConfirmed)
            .Select(address => address.Email).ToListAsync();
        // A confirmed primary identity can predate the additional-email table.
        var user = await _database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actor.Id);
        if (user?.EmailConfirmed == true && user.Email is not null) addresses.Add(user.Email);
        var active = await _tgs.GetActiveTgsStaff() ?? [];
        var acting = active.Where(person => addresses.Contains(person.Email ?? "", StringComparer.OrdinalIgnoreCase)).ToList();
        if (acting.Count != 1) return "Your confirmed email address must match one active agronomist record to complete QA.";
        if (acting.Any(person => person.StaffId == source.Survey.AgronomistId))
            return "Another agronomist must QA this report. The survey agronomist cannot approve their own report.";
        if (document.SampleMatches.Concat(document.Tissue.Matches).Any(match => match.ConfirmedById == actor.Id
            || match.ConfirmedBy.Length > 0 && addresses.Contains(match.ConfirmedBy, StringComparer.OrdinalIgnoreCase)))
            return "Another agronomist must QA this revision because you confirmed its sample matches.";
        // Peer QA also excludes a different agronomist who prepared or submitted this exact revision, including administrators.
        var contributors = await _database.SoilReportAudits.AsNoTracking()
            .Where(item => item.RevisionId == document.Id && (item.Action == "edit" || item.Action == "submit"))
            .Select(item => new { item.ActorId, item.Actor }).ToListAsync();
        if (contributors.Any(item => item.ActorId == actor.Id || addresses.Contains(item.Actor, StringComparer.OrdinalIgnoreCase)))
            return "Another agronomist must QA this revision because you prepared or submitted it.";
        return "";
    }

    private static void CheckExpected(SoilReportState state, Guid revisionId, long version)
    {
        if (state.CurrentRevisionId != revisionId || state.Version != version) throw new DbUpdateConcurrencyException();
    }
    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ArgumentException("Enter a reason of 1–1000 characters for the audit history.");
    }

    private static string ReviewContent(SoilReportDocument document) => JsonSerializer.Serialize(new
    {
        document.Actions,
        // Compare the final recommendations, including reasons and restored rates. Resubmitting existing
        // overrides in a different order must not turn a section-only change into an agronomic edit.
        Blocks = document.Blocks.Select(block => new
        {
            block.LabSoilId, block.Excluded,
            Recommendations = block.Recommendations.OrderBy(item => item.Product, StringComparer.OrdinalIgnoreCase)
        })
    });

    private static string EditReason(string reason, SoilReportOptions previousOptions, string previousReviewContent, SoilReportDocument document)
    {
        var options = document.Options;
        (string Name, bool Before, bool After)[] sections =
        [
            ("Soil", previousOptions.IncludeSoil, options.IncludeSoil),
            ("Tissue", previousOptions.IncludeTissue, options.IncludeTissue),
            ("Status overview", previousOptions.ShowStatusOverview, options.ShowStatusOverview),
            ("Block results", previousOptions.ShowBlockDetails, options.ShowBlockDetails),
            ("Theoretical amounts", previousOptions.ShowRequiredAmounts, options.ShowRequiredAmounts),
            ("Historical Farm Averages", previousOptions.ShowHistory, options.ShowHistory),
            ("Sugarcane inputs", previousOptions.ShowSugarInputs, options.ShowSugarInputs),
            ("Individual block details", previousOptions.ShowIndividualBlockDetails, options.ShowIndividualBlockDetails)
        ];
        var changes = sections.Where(section => section.Before != section.After)
            .Select(section => $"{section.Name} {(section.After ? "enabled" : "disabled")}").ToList();
        // A layout-only save can explain itself. Determine this on the server after validation, so a
        // section toggle cannot bypass reasons for changed advice, rates, exclusions or a no-change review.
        if (string.IsNullOrWhiteSpace(reason) && (reason?.Length ?? 0) <= 1000 && changes.Count > 0
            && previousReviewContent == ReviewContent(document))
            return "Report sections changed: " + string.Join("; ", changes) + ".";
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Enter an agronomist review note (1–1000 characters). A note is optional only when changing report contents or sections.");
        RequireReason(reason);
        return reason;
    }
    private static SoilReportSettings ToSettings(SoilReportState state) => new() { SurveyId = state.SurveyId,
        TemplateId = state.TemplateId, RegionId = 1, Version = state.Version,
        TissueTemplates = Deserialize<List<TissueTemplateSelection>>(state.TissueTemplatesJson) };
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    public static void ApplyEdit(SoilReportDocument document, SoilReportSource source, SoilReportEdit edit)
    {
        if (edit.Actions is null || edit.Options is null || edit.Overrides is null || edit.ExcludedLabSoilIds is null)
            throw new ArgumentException("Supply actions, display options, recommendations and block choices.");
        // Enforce the same invariant as the review switches for direct API callers as well.
        if (!edit.Options.IncludeSoil && !edit.Options.IncludeTissue)
            throw new ArgumentException("Select at least one report material: Soil or Tissue.");
        if (!edit.Options.IncludeSoil && !document.Tissue.Samples.Any(sample => sample.SoilLabIds.Any(id =>
                document.Blocks.Any(block => block.LabSoilId == id && !edit.ExcludedLabSoilIds.Contains(id)))))
            throw new ArgumentException("Retain at least one block with tissue results, or include Soil in the report.");
        if (edit.Actions.Count > 50 || edit.Actions.Any(item => item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120
                || string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 5000))
            throw new ArgumentException("Each action needs a title (up to 120 characters) and text (up to 5000 characters); use at most 50 groups.");
        if (edit.ExcludedLabSoilIds.Any(id => document.Blocks.All(block => block.LabSoilId != id))
            || document.Blocks.All(block => edit.ExcludedLabSoilIds.Contains(block.LabSoilId)))
            throw new ArgumentException("Choose valid blocks and retain at least one block in the report.");
        // Reject incomplete API payloads before normalizing keys or changing the in-memory revision.
        if (edit.Overrides.Any(item => item is null || string.IsNullOrWhiteSpace(item.Product)))
            throw new ArgumentException("Choose a product for every recommendation adjustment.");
        if (edit.Overrides.GroupBy(item => (item.LabSoilId, Product: item.Product.Trim().ToUpperInvariant())).Any(group => group.Count() > 1))
            throw new ArgumentException("Each block can contain a product only once.");
        document.Actions = edit.Actions; document.Options = edit.Options;
        foreach (var block in document.Blocks)
        {
            block.Excluded = edit.ExcludedLabSoilIds.Contains(block.LabSoilId);
            // Rebuild from the immutable calculated rates so removing an override actually restores the calculation.
            block.Recommendations.RemoveAll(item => item.CalculatedRate == 0 && item.Overridden);
            foreach (var recommendation in block.Recommendations)
            { recommendation.Rate = recommendation.CalculatedRate; recommendation.Overridden = false; recommendation.Reason = ""; }
        }
        foreach (var change in edit.Overrides)
        {
            var block = document.Blocks.SingleOrDefault(block => block.LabSoilId == change.LabSoilId)
                ?? throw new ArgumentException("A recommendation refers to an unknown block.");
            var product = source.Products.FirstOrDefault(item => string.Equals(item.ProductName, change.Product, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Choose an existing soil recommendation product.");
            if (!double.IsFinite(change.Rate) || change.Rate < 0 || change.Rate > 100000 || string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Length > 1000)
                throw new ArgumentException("Each override needs a valid non-negative kg/ha rate and a reason of up to 1000 characters.");
            var recommendation = block.Recommendations.FirstOrDefault(item => item.Product.Equals(product.ProductName, StringComparison.OrdinalIgnoreCase));
            if (recommendation is null) { recommendation = new() { Product = product.ProductName, Order = product.DisplayOrder }; block.Recommendations.Add(recommendation); }
            recommendation.Rate = change.Rate; recommendation.Overridden = true; recommendation.Reason = change.Reason.Trim();
        }
    }
}
