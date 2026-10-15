using System.Security.Claims;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Authorize]
[Route("report-preferences")]
public sealed class ReportPreferencesController(ApplicationDbContext _database, ReportBrandPreferenceStore _preferences) : ControllerBase
{
    // Derive ownership from the authenticated identity; clients cannot choose another user.
    [HttpGet]
    public async Task<ActionResult<ReportBrandPreference>> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await _database.Users.AnyAsync(user => user.Id == userId)) return Unauthorized();
        return Ok(new ReportBrandPreference { BrandKey = await _preferences.GetAsync(userId) });
    }

    [HttpPut]
    public async Task<IActionResult> Save(ReportBrandPreference preference)
    {
        if (!ReportBrandPreference.IsSupported(preference.BrandKey)) return BadRequest("Unsupported report brand.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await _database.Users.AnyAsync(user => user.Id == userId)) return Unauthorized();
        await _preferences.SaveAsync(userId, preference.BrandKey);
        return NoContent();
    }
}
