using System.Security.Claims;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("soil-report-definitions")]
[Authorize(Roles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager)]
public sealed class SoilReportDefinitionsController(ApplicationDbContext _database, SoilReportDefinitionStore _store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SoilReportDefinition>>> Get(CancellationToken cancellationToken) => Ok(await _store.ReadAsync(cancellationToken));

    [HttpGet("fields")]
    public async Task<ActionResult<List<SoilDefinitionField>>> Fields(CancellationToken cancellationToken) =>
        Ok(await _database.SoilReportDefinitionFields.AsNoTracking().OrderBy(field => field.Label).ToListAsync(cancellationToken));

    [HttpGet("{key}/history")]
    public async Task<ActionResult<List<SoilDefinitionChange>>> History(string key, CancellationToken cancellationToken) =>
        Ok(await _database.SoilReportDefinitionAudits.AsNoTracking().Where(change => change.DefinitionKey == key)
            .OrderByDescending(change => change.ChangedUtc).Take(100)
            .Select(change => new SoilDefinitionChange(change.Id, change.ChangedUtc, change.ChangedBy, change.Reason, change.Version)).ToListAsync(cancellationToken));

    [HttpPut("{key}")]
    public async Task<ActionResult<SoilReportDefinition>> Save(string key, SaveSoilDefinition request, CancellationToken cancellationToken)
    {
        if (request.Definition is null || key != request.Definition.Key) return BadRequest("The definition key cannot change.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await _database.Users.AnyAsync(user => user.Id == userId, cancellationToken)) return Unauthorized();
        try
        {
            // Identity is obtained from the token, never from editable client fields.
            return Ok(await _store.SaveAsync(request, userId, User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? userId, cancellationToken));
        }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        catch (KeyNotFoundException exception) { return NotFound(exception.Message); }
        catch (DbUpdateConcurrencyException) { return Conflict("Another person changed this definition. Reload before editing."); }
    }
}
