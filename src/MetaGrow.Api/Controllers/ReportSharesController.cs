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

        var token = tokens.CreateToken();
        var share = new ReportShare
        {
            Id = Guid.NewGuid(),
            SurveyId = request.SurveyId,
            ReportArea = request.ReportArea.ToLowerInvariant(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"Survey {request.SurveyId}" : request.Name.Trim(),
            TokenHash = tokens.HashToken(token),
            ProtectedToken = tokens.ProtectToken(token),
            CreatedByUserId = userId,
            CreatedByEmail = email,
            CreatedUtc = DateTime.UtcNow
        };

        database.ReportShares.Add(share);
        await database.SaveChangesAsync();
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
    public async Task<ActionResult<MetaGrowReportShareResolveResponse>> Resolve(
        Guid id,
        MetaGrowReportShareResolveRequest request)
    {
        var share = await database.ReportShares.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.RevokedUtc == null);
        if (share is null || !tokens.TokenMatches(request.Token, share.TokenHash)) return NotFound();

        var now = DateTime.UtcNow;
        var updated = await database.ReportShares
            .Where(item => item.Id == id && item.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastViewedUtc, now)
                .SetProperty(item => item.ViewCount, item => item.ViewCount + 1));
        if (updated == 0) return NotFound();

        return Ok(new MetaGrowReportShareResolveResponse
        {
            SurveyId = share.SurveyId,
            ReportArea = share.ReportArea
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
