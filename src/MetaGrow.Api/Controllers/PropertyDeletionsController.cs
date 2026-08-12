using System.Net.Http.Headers;
using System.Security.Claims;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("property-deletions")]
public sealed class PropertyDeletionsController(
    ApplicationDbContext database,
    ITgsApiService tgsApi,
    ILogger<PropertyDeletionsController> logger) : ControllerBase
{
    private const string StaffRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," + MetaGrowRoles.Agronomist;
    private const string ReviewerRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager;

    [Authorize(Roles = StaffRoles)]
    [HttpGet("pending")]
    public async Task<ActionResult<MetaGrowPropertyDeletionDto[]>> GetPending()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();

        var query = database.PropertyDeletionRequests.AsNoTracking()
            .Where(request => request.Status == MetaGrowPropertyDeletionStatus.Pending ||
                              request.Status == MetaGrowPropertyDeletionStatus.Processing);
        if (!IsReviewer()) query = query.Where(request => request.RequestedByUserId == userId);

        var requests = await query
            .OrderBy(request => request.RequestedUtc)
            .ToListAsync();
        return Ok(requests.Select(ToDto).ToArray());
    }

    [Authorize(Roles = MetaGrowRoles.Agronomist)]
    [HttpPost]
    public async Task<ActionResult<MetaGrowPropertyDeletionDto>> Create(
        MetaGrowPropertyDeletionCreateRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return Forbid();

        var preflight = await GetEligibleProperty(request.PropertyId);
        if (preflight.Result is not null) return preflight.Result;

        var existing = await database.PropertyDeletionRequests.AsNoTracking()
            .AnyAsync(item => item.PropertyId == request.PropertyId &&
                              (item.Status == MetaGrowPropertyDeletionStatus.Pending ||
                               item.Status == MetaGrowPropertyDeletionStatus.Processing));
        if (existing) return Conflict(Error("A deletion request is already waiting for this property."));

        var deletionRequest = new PropertyDeletionRequest
        {
            Id = Guid.NewGuid(),
            PropertyId = preflight.Value!.PropertyId,
            PropertyName = preflight.Value.PropertyName,
            Status = MetaGrowPropertyDeletionStatus.Pending,
            RequestedByUserId = userId,
            RequestedByEmail = email,
            RequestedUtc = DateTime.UtcNow
        };

        database.PropertyDeletionRequests.Add(deletionRequest);
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Duplicate property deletion request for {PropertyId}", request.PropertyId);
            return Conflict(Error("A deletion request is already waiting for this property."));
        }

        return Ok(ToDto(deletionRequest));
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{propertyId:int}/delete")]
    public async Task<ActionResult<MetaGrowPropertyDeletionResult>> DeleteImmediately(int propertyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || token is null) return Forbid();

        var result = await tgsApi.DeleteEligibleProperty(propertyId, token);
        if (result is null)
            return Conflict(Error(tgsApi.ErrorMessage ?? "The property could not be deleted."));

        database.PropertyDeletionRequests.Add(new PropertyDeletionRequest
        {
            Id = Guid.NewGuid(),
            PropertyId = result.PropertyId,
            PropertyName = result.PropertyName,
            Status = MetaGrowPropertyDeletionStatus.Completed,
            RequestedByUserId = userId,
            RequestedByEmail = email,
            RequestedUtc = DateTime.UtcNow,
            ReviewedByUserId = userId,
            ReviewedByEmail = email,
            ReviewedUtc = DateTime.UtcNow,
            ReviewNote = "Deleted directly by an authorised reviewer."
        });
        try
        {
            await database.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // The TGS deletion has already committed. Preserve the successful response and
            // make the missing secondary audit conspicuous in the application log.
            logger.LogError(exception, "Property {PropertyId} was deleted but its MetaGrow audit row could not be saved", propertyId);
        }

        return Ok(result);
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<MetaGrowPropertyDeletionDto>> Approve(
        Guid id,
        MetaGrowPropertyDeletionReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail) || token is null)
            return Forbid();

        var claimed = await database.PropertyDeletionRequests
            .Where(item => item.Id == id && item.Status == MetaGrowPropertyDeletionStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, MetaGrowPropertyDeletionStatus.Processing)
                .SetProperty(item => item.ReviewedByUserId, reviewerId)
                .SetProperty(item => item.ReviewedByEmail, reviewerEmail)
                .SetProperty(item => item.ReviewedUtc, DateTime.UtcNow)
                .SetProperty(item => item.ReviewNote, Clean(request.Note))
                .SetProperty(item => item.LastError, (string?)null));
        if (claimed == 0) return Conflict(Error("This deletion request is no longer awaiting approval."));

        var deletionRequest = await database.PropertyDeletionRequests.SingleAsync(item => item.Id == id);
        var result = await tgsApi.DeleteEligibleProperty(deletionRequest.PropertyId, token);
        if (result is null)
        {
            deletionRequest.Status = MetaGrowPropertyDeletionStatus.Pending;
            deletionRequest.ReviewedByUserId = null;
            deletionRequest.ReviewedByEmail = null;
            deletionRequest.ReviewedUtc = null;
            deletionRequest.LastError = Truncate(tgsApi.ErrorMessage ?? "The property could not be deleted.", 1000);
            await database.SaveChangesAsync();
            return Conflict(Error(deletionRequest.LastError));
        }

        deletionRequest.Status = MetaGrowPropertyDeletionStatus.Completed;
        deletionRequest.PropertyName = result.PropertyName;
        deletionRequest.LastError = null;
        await database.SaveChangesAsync();
        return Ok(ToDto(deletionRequest));
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<MetaGrowPropertyDeletionDto>> Reject(
        Guid id,
        MetaGrowPropertyDeletionReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail)) return Forbid();

        var deletionRequest = await database.PropertyDeletionRequests
            .SingleOrDefaultAsync(item => item.Id == id && item.Status == MetaGrowPropertyDeletionStatus.Pending);
        if (deletionRequest is null)
            return Conflict(Error("This deletion request is no longer awaiting approval."));

        deletionRequest.Status = MetaGrowPropertyDeletionStatus.Rejected;
        deletionRequest.ReviewedByUserId = reviewerId;
        deletionRequest.ReviewedByEmail = reviewerEmail;
        deletionRequest.ReviewedUtc = DateTime.UtcNow;
        deletionRequest.ReviewNote = Clean(request.Note);
        deletionRequest.LastError = null;
        await database.SaveChangesAsync();
        return Ok(ToDto(deletionRequest));
    }

    private async Task<(PropertyDependencySummary? Value, ActionResult? Result)> GetEligibleProperty(int propertyId)
    {
        var summaries = await tgsApi.GetPropertyDependencySummaries([propertyId]);
        if (summaries is null)
            return (null, StatusCode(StatusCodes.Status502BadGateway,
                Error(tgsApi.ErrorMessage ?? "The property safety check could not be completed.")));

        var summary = summaries.SingleOrDefault(item => item.PropertyId == propertyId);
        if (summary is null) return (null, NotFound(Error("The property could not be found.")));
        if (!summary.PhysicalDeleteAllowed)
            return (null, Conflict(Error("This property has linked data and must be merged instead.")));

        return (summary, null);
    }

    private bool IsReviewer() =>
        User.IsInRole(MetaGrowRoles.Admin) || User.IsInRole(MetaGrowRoles.AgricultureManager);

    private string? AccessToken()
    {
        return AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) &&
               authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? authorization.Parameter
            : null;
    }

    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private static MetaGrowPropertyDeletionDto ToDto(PropertyDeletionRequest request) => new()
    {
        Id = request.Id,
        PropertyId = request.PropertyId,
        PropertyName = request.PropertyName,
        Status = request.Status,
        RequestedByEmail = request.RequestedByEmail,
        RequestedUtc = request.RequestedUtc,
        ReviewedByEmail = request.ReviewedByEmail,
        ReviewedUtc = request.ReviewedUtc,
        ReviewNote = request.ReviewNote,
        LastError = request.LastError
    };
}
