using System.Security.Claims;
using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController, Route("soil-reports")]
[Authorize(Roles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," + MetaGrowRoles.Agronomist)]
public sealed class SoilReportsController(SoilReportService _reports, ApplicationDbContext _database) : ControllerBase
{
    private string AccessToken => Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    private SoilReportActor Actor => new(User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "", User.IsInRole(MetaGrowRoles.Admin));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string status = "Awaiting QA")
    {
        if (status is not ("Awaiting QA" or "Editing" or "Ready for QA" or "Approved")) return BadRequest("Choose a report status.");
        // Bound the queue; raw source snapshots are never loaded for a report listing.
        var revisions = await (from state in _database.SoilReportStates.AsNoTracking()
            join revision in _database.SoilReportRevisions.AsNoTracking() on state.CurrentRevisionId equals revision.Id
            where state.Status == status orderby revision.CreatedUtc descending
            select revision.DocumentJson).Take(200).ToListAsync();
        return Ok(revisions.Select(json => JsonSerializer.Deserialize<SoilReportDocument>(json)!)
            .Select(document => new SoilReportListItem(document.SurveyId, document.PropertyName, document.SurveyDate, document.Agronomist, status, document.Revision)));
    }

    // Opening is a POST because a changed calculation may append a revision and audit record in MetaGrow.
    [HttpPost("{surveyId:int:min(1)}/open")]
    public Task<IActionResult> Open(int surveyId) => Run(async () => await _reports.Open(surveyId, Actor, AccessToken));
    [HttpGet("options")]
    public Task<IActionResult> Options() => Run(async () => await _reports.Options(AccessToken));
    [HttpGet("{surveyId:int:min(1)}/settings")]
    public Task<IActionResult> Settings(int surveyId) => Run(async () => await _reports.Settings(surveyId, AccessToken));
    [HttpGet("{surveyId:int:min(1)}/tissue-matching")]
    public Task<IActionResult> TissueMatching(int surveyId) => Run(async () => await _reports.Matching(surveyId, AccessToken, true));
    [HttpPut("{surveyId:int:min(1)}/tissue-matching")]
    public Task<IActionResult> TissueMatching(int surveyId, SoilReportMatchingChange change) =>
        Run(async () => await _reports.SaveMatching(surveyId, change, Actor, AccessToken, true));
    [HttpGet("{surveyId:int:min(1)}/preparation")]
    public Task<IActionResult> Preparation(int surveyId) => Run(async () => await _reports.Preparation(surveyId, AccessToken));
    [HttpGet("{surveyId:int:min(1)}/matching")]
    public Task<IActionResult> Matching(int surveyId) => Run(async () => await _reports.Matching(surveyId, AccessToken));
    [HttpPut("{surveyId:int:min(1)}/matching")]
    public Task<IActionResult> Matching(int surveyId, SoilReportMatchingChange change) =>
        Run(async () => await _reports.SaveMatching(surveyId, change, Actor, AccessToken));
    [HttpPut("{surveyId:int:min(1)}/not-sampled")]
    public Task<IActionResult> NotSampled(int surveyId, SoilReportNotSampledChange change) =>
        Run(async () => await _reports.SaveNotSampled(surveyId, change, Actor, AccessToken));
    [HttpPut("{surveyId:int:min(1)}/settings")]
    public Task<IActionResult> Settings(int surveyId, SoilReportSettings settings) => Run(async () =>
    {
        if (settings.SurveyId != surveyId) throw new ArgumentException("The survey cannot change.");
        return await _reports.SaveSettings(settings, Actor, AccessToken);
    });
    [HttpPut("{surveyId:int:min(1)}")]
    public Task<IActionResult> Edit(int surveyId, SoilReportEdit edit) => Run(async () => await _reports.Edit(surveyId, edit, Actor));
    [HttpPost("{surveyId:int:min(1)}/workflow")]
    public Task<IActionResult> Transition(int surveyId, SoilReportTransition request) => Run(async () => await _reports.Transition(surveyId, request, Actor, AccessToken));
    [HttpGet("{surveyId:int:min(1)}/history")]
    public async Task<IActionResult> History(int surveyId) => Ok(await _database.SoilReportAudits.AsNoTracking()
        .Where(item => item.SurveyId == surveyId).OrderByDescending(item => item.Id)
        .Select(item => new SoilReportAuditEntry(item.Id, item.RevisionId, item.OccurredUtc, item.Actor,
            item.Action, item.Reason, item.PreviousStatus, item.Status, item.QaHours)).ToListAsync());
    [HttpGet("{surveyId:int:min(1)}/revisions")]
    public async Task<IActionResult> Revisions(int surveyId) => Ok(await _database.SoilReportRevisions.AsNoTracking()
        .Where(item => item.SurveyId == surveyId).OrderByDescending(item => item.Number)
        .Select(item => new SoilReportRevisionSummary(item.Id, item.Number, item.CreatedUtc, item.CreatedBy, item.InputHash)).ToListAsync());
    [HttpGet("{surveyId:int:min(1)}/revisions/{id:guid}")]
    public async Task<IActionResult> Revision(int surveyId, Guid id)
    {
        var revision = await _database.SoilReportRevisions.AsNoTracking().SingleOrDefaultAsync(item => item.SurveyId == surveyId && item.Id == id);
        if (revision is null) return NotFound();
        var document = JsonSerializer.Deserialize<SoilReportDocument>(revision.DocumentJson)!;
        document.Status = await _database.SoilReportAudits.Where(item => item.RevisionId == id).OrderByDescending(item => item.Id)
            .Select(item => item.Status).FirstOrDefaultAsync() ?? "Editing";
        return Ok(document);
    }

    private async Task<IActionResult> Run(Func<Task<object>> operation)
    {
        if (string.IsNullOrWhiteSpace(Actor.Id) || !await _database.Users.AnyAsync(user => user.Id == Actor.Id)) return Unauthorized();
        try { return Ok(await operation()); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        catch (DbUpdateConcurrencyException) { return Conflict("Another person changed this report. Reload before making further changes."); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }
}
