using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("property-merges")]
public sealed class PropertyMergesController(
    ApplicationDbContext database,
    ITgsApiService tgsApi,
    ILogger<PropertyMergesController> logger) : ControllerBase
{
    private const string StaffRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," + MetaGrowRoles.Agronomist;
    private const string ReviewerRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Authorize(Roles = StaffRoles)]
    [HttpGet("pending")]
    public async Task<ActionResult<MetaGrowPropertyMergeRequestDto[]>> GetPending()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();

        var query = database.PropertyMergeRequests.AsNoTracking()
            .Where(request => request.Status == MetaGrowPropertyMergeStatus.Pending ||
                              request.Status == MetaGrowPropertyMergeStatus.Processing);
        if (!IsReviewer()) query = query.Where(request => request.RequestedByUserId == userId);

        var requests = await query.OrderBy(request => request.RequestedUtc).ToListAsync();
        return Ok(requests.Select(ToDto).ToArray());
    }

    [Authorize(Roles = MetaGrowRoles.Agronomist)]
    [HttpPost]
    public async Task<ActionResult<MetaGrowPropertyMergeRequestDto>> Create(
        MetaGrowPropertyMergeCreateRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return Forbid();

        var preview = await tgsApi.GetPropertyMergePreview(request.Plan);
        if (preview is null)
            return StatusCode(StatusCodes.Status502BadGateway,
                Error(tgsApi.ErrorMessage ?? "The live merge safety check could not be completed."));
        if (!preview.CanRequestMerge)
            return Conflict(Error("The merge plan has unresolved blocks or data collisions. Review it before requesting approval."));

        var plan = CanonicalPlan(request.Plan, preview);
        var planJson = JsonSerializer.Serialize(plan, JsonOptions);
        var existing = await database.PropertyMergeRequests.AsNoTracking()
            .AnyAsync(item => item.SourcePropertyId == plan.SourcePropertyId &&
                              (item.Status == MetaGrowPropertyMergeStatus.Pending ||
                               item.Status == MetaGrowPropertyMergeStatus.Processing));
        if (existing)
            return Conflict(Error("A merge request is already waiting for this source property."));

        var mergeRequest = new PropertyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourcePropertyId = preview.Source.PropertyId,
            SourcePropertyName = preview.Source.PropertyName,
            TargetPropertyId = preview.Target.PropertyId,
            TargetPropertyName = preview.Target.PropertyName,
            PlanJson = planJson,
            PlanHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planJson))),
            Status = MetaGrowPropertyMergeStatus.Pending,
            RequestedByUserId = userId,
            RequestedByEmail = email,
            RequestedUtc = DateTime.UtcNow
        };

        database.PropertyMergeRequests.Add(mergeRequest);
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Duplicate property merge request for source {PropertyId}", plan.SourcePropertyId);
            return Conflict(Error("A merge request is already waiting for this source property."));
        }

        return Ok(ToDto(mergeRequest));
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<MetaGrowPropertyMergeRequestDto>> Reject(
        Guid id,
        MetaGrowPropertyMergeReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail)) return Forbid();

        var mergeRequest = await database.PropertyMergeRequests
            .SingleOrDefaultAsync(item => item.Id == id && item.Status == MetaGrowPropertyMergeStatus.Pending);
        if (mergeRequest is null)
            return Conflict(Error("This merge request is no longer awaiting approval."));

        mergeRequest.Status = MetaGrowPropertyMergeStatus.Rejected;
        mergeRequest.ReviewedByUserId = reviewerId;
        mergeRequest.ReviewedByEmail = reviewerEmail;
        mergeRequest.ReviewedUtc = DateTime.UtcNow;
        mergeRequest.ReviewNote = Clean(request.Note);
        mergeRequest.LastError = null;
        await database.SaveChangesAsync();
        return Ok(ToDto(mergeRequest));
    }

    private bool IsReviewer() =>
        User.IsInRole(MetaGrowRoles.Admin) || User.IsInRole(MetaGrowRoles.AgricultureManager);

    private static PropertyMergePreviewRequest CanonicalPlan(
        PropertyMergePreviewRequest supplied,
        PropertyMergePreview preview) => new()
    {
        SourcePropertyId = preview.Source.PropertyId,
        TargetPropertyId = preview.Target.PropertyId,
        BlockDecisions = preview.Blocks
            .OrderBy(block => block.BlockType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(block => block.SourceBlockId)
            .Select(block => new PropertyMergeBlockDecision
            {
                BlockType = block.BlockType,
                SourceBlockId = block.SourceBlockId,
                Action = block.Action,
                TargetBlockId = block.Action == PropertyMergeBlockAction.Merge ? block.TargetBlockId : null
            })
            .ToList(),
        FieldChoices = preview.FieldDifferences
            .OrderBy(field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(field => new PropertyMergeFieldChoice
            {
                FieldName = field.FieldName,
                ValueSource = field.ValueSource
            })
            .ToList()
    };

    private static MetaGrowPropertyMergeRequestDto ToDto(PropertyMergeRequest request)
    {
        PropertyMergePreviewRequest? plan = null;
        try { plan = JsonSerializer.Deserialize<PropertyMergePreviewRequest>(request.PlanJson, JsonOptions); }
        catch (JsonException) { }

        return new MetaGrowPropertyMergeRequestDto
        {
            Id = request.Id,
            SourcePropertyId = request.SourcePropertyId,
            SourcePropertyName = request.SourcePropertyName,
            TargetPropertyId = request.TargetPropertyId,
            TargetPropertyName = request.TargetPropertyName,
            Status = request.Status,
            BlockDecisions = plan?.BlockDecisions ?? [],
            FieldChoices = plan?.FieldChoices ?? [],
            RequestedByEmail = request.RequestedByEmail,
            RequestedUtc = request.RequestedUtc,
            ReviewedByEmail = request.ReviewedByEmail,
            ReviewedUtc = request.ReviewedUtc,
            ReviewNote = request.ReviewNote,
            LastError = request.LastError
        };
    }

    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
