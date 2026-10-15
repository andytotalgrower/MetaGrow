using MetaGrow.Shared.Models;
using System.Security.Claims;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("report-shares")]
public sealed class ReportSharesController(
    ApplicationDbContext database,
    IReportShareTokenService tokens) : ControllerBase
{
    private const string StaffRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," + MetaGrowRoles.Agronomist;

    [Authorize(Roles = StaffRoles)]
    [HttpGet("survey/{surveyId:int}")]
    public async Task<ActionResult<MetaGrowReportShareDto[]>> GetForSurvey(
        int surveyId,
        [FromQuery] string reportArea = MetaGrowReportAreas.MultiCrop)
    {
        if (!MetaGrowReportAreas.IsSupported(reportArea)) return BadRequest("Unsupported report area.");

        var shares = await database.ReportShares.AsNoTracking()
            .Where(share => share.SurveyId == surveyId && share.ReportArea == reportArea.ToLowerInvariant())
            .OrderByDescending(share => share.CreatedUtc)
            .ToListAsync();

        return Ok(shares.Select(share => ToDto(share)).ToArray());
    }

    [Authorize(Roles = StaffRoles)]
    [HttpPost]
    public async Task<ActionResult<MetaGrowReportShareDto>> Create(MetaGrowReportShareCreateRequest request)
    {
        if (!MetaGrowReportAreas.IsSupported(request.ReportArea)) return BadRequest("Unsupported report area.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return Forbid();

        if (request.ReportArea.Equals(MetaGrowReportAreas.Soil, StringComparison.OrdinalIgnoreCase))
        {
            var soil = await database.SoilReportStates.SingleOrDefaultAsync(item => item.SurveyId == request.SurveyId);
            if (soil?.Status != "Approved" || !request.SoilRevisionId.HasValue || soil.CurrentRevisionId != request.SoilRevisionId)
                return BadRequest("Only the current approved soil report revision can be shared. Refresh the report first.");
            // The approved revision remains immutable even if another user starts a later edit while this link is created.
            // Sharing therefore does not invalidate the report editor's optimistic-concurrency version.
        }

        var token = tokens.CreateToken();
        var share = new ReportShare
        {
            Id = Guid.NewGuid(),
            SurveyId = request.SurveyId,
            SoilRevisionId = request.ReportArea.Equals(MetaGrowReportAreas.Soil, StringComparison.OrdinalIgnoreCase) ? request.SoilRevisionId : null,
            ReportArea = request.ReportArea.ToLowerInvariant(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"Survey {request.SurveyId}" : request.Name.Trim(),
            TokenHash = tokens.HashToken(token),
            ProtectedToken = tokens.ProtectToken(token),
            CreatedByUserId = userId,
            CreatedByEmail = email,
            CreatedUtc = DateTime.UtcNow
        };

        database.ReportShares.Add(share);
        try { await database.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict("The report changed while creating the link. Refresh before sharing."); }
        return Ok(ToDto(share, token));
    }

    [Authorize(Roles = StaffRoles)]
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var share = await database.ReportShares.SingleOrDefaultAsync(item => item.Id == id);
        if (share is null) return NotFound();
        if (share.RevokedUtc is not null) return NoContent();

        share.RevokedUtc = DateTime.UtcNow;
        share.RevokedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        share.RevokedByEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        await database.SaveChangesAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("report-share")]
    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<SharedReportAccess>> Resolve(
        Guid id,
        MetaGrowReportShareResolveRequest request)
    {
        var share = await database.ReportShares.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.RevokedUtc == null);
        if (share is null || !tokens.TokenMatches(request.Token, share.TokenHash)) return NotFound();

        SoilReportDocument? soilDocument = null;
        if (share.ReportArea == MetaGrowReportAreas.Soil)
        {
            var revision = await database.SoilReportRevisions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == share.SoilRevisionId && item.SurveyId == share.SurveyId);
            if (revision is null || !await database.SoilReportAudits.AnyAsync(item => item.RevisionId == revision.Id && item.Action == "approve")) return NotFound();
            soilDocument = System.Text.Json.JsonSerializer.Deserialize<SoilReportDocument>(revision.DocumentJson)!;
            soilDocument.Status = "Approved";
            // Public reports contain the result and application notes; internal calculation operands remain staff-only.
            soilDocument.Trace = []; soilDocument.DefinitionSnapshot = [];
            // Matching evidence and staff attribution are internal QA/audit information.
            soilDocument.SampleMatches = [];
            // Tissue matching reasons and confirmer identities are internal audit evidence too.
            soilDocument.Tissue.Matches = [];
        }

        var now = DateTime.UtcNow;
        var updated = await database.ReportShares
            .Where(item => item.Id == id && item.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastViewedUtc, now)
                .SetProperty(item => item.ViewCount, item => item.ViewCount + 1));
        if (updated == 0) return NotFound();

        return Ok(new SharedReportAccess
        {
            SurveyId = share.SurveyId,
            ReportArea = share.ReportArea,
            SoilReport = soilDocument,
            // Use the creator's current preference only after validating this share token.
            BrandKey = await new ReportBrandPreferenceStore(database).GetAsync(share.CreatedByUserId)
        });
    }

    private MetaGrowReportShareDto ToDto(ReportShare share, string? token = null) => new()
    {
        Id = share.Id,
        SurveyId = share.SurveyId,
        ReportArea = share.ReportArea,
        Name = share.Name,
        Token = share.RevokedUtc is null
            ? token ?? tokens.TryUnprotectToken(share.ProtectedToken)
            : null,
        CreatedUtc = share.CreatedUtc,
        CreatedByEmail = share.CreatedByEmail,
        RevokedUtc = share.RevokedUtc,
        RevokedByEmail = share.RevokedByEmail,
        LastViewedUtc = share.LastViewedUtc,
        ViewCount = share.ViewCount
    };
}
