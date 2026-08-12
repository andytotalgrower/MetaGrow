using System.Net.Http.Headers;
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
    [HttpPost("execute")]
    public async Task<ActionResult<MetaGrowPropertyMergeRequestDto>> ExecuteImmediately(
        MetaGrowPropertyMergeExecuteRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail) || token is null)
            return Forbid();

        var preview = await tgsApi.GetPropertyMergePreview(request.Plan);
        if (preview is null)
            return StatusCode(StatusCodes.Status502BadGateway,
                Error(tgsApi.ErrorMessage ?? "The live merge safety check could not be completed."));
        if (!preview.CanRequestMerge)
            return Conflict(Error("The merge plan has unresolved blocks or data collisions. Review it before merging."));

        var plan = CanonicalPlan(request.Plan, preview);
        var planJson = JsonSerializer.Serialize(plan, JsonOptions);
        var existing = await database.PropertyMergeRequests.AsNoTracking()
            .AnyAsync(item => item.SourcePropertyId == plan.SourcePropertyId &&
                              (item.Status == MetaGrowPropertyMergeStatus.Pending ||
                               item.Status == MetaGrowPropertyMergeStatus.Processing));
        if (existing)
            return Conflict(Error("A merge request is already waiting for this source property. Review that request instead."));

        var now = DateTime.UtcNow;
        var mergeRequest = new PropertyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourcePropertyId = preview.Source.PropertyId,
            SourcePropertyName = preview.Source.PropertyName,
            TargetPropertyId = preview.Target.PropertyId,
            TargetPropertyName = preview.Target.PropertyName,
            PlanJson = planJson,
            PlanHash = Hash(planJson),
            Status = MetaGrowPropertyMergeStatus.Processing,
            RequestedByUserId = reviewerId,
            RequestedByEmail = reviewerEmail,
            RequestedUtc = now,
            ReviewedByUserId = reviewerId,
            ReviewedByEmail = reviewerEmail,
            ReviewedUtc = now,
            ReviewNote = Clean(request.Note) ?? "Merged directly by an authorised reviewer."
        };
        database.PropertyMergeRequests.Add(mergeRequest);
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Concurrent property merge request for source {PropertyId}", plan.SourcePropertyId);
            return Conflict(Error("A merge request is already waiting for this source property."));
        }

        return await ExecuteClaimed(mergeRequest, plan, token, reviewerId, reviewerEmail);
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<MetaGrowPropertyMergeRequestDto>> Approve(
        Guid id,
        MetaGrowPropertyMergeReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail) || token is null)
            return Forbid();

        var now = DateTime.UtcNow;
        var claimed = await database.PropertyMergeRequests
            .Where(item => item.Id == id && item.Status == MetaGrowPropertyMergeStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, MetaGrowPropertyMergeStatus.Processing)
                .SetProperty(item => item.ReviewedByUserId, reviewerId)
                .SetProperty(item => item.ReviewedByEmail, reviewerEmail)
                .SetProperty(item => item.ReviewedUtc, now)
                .SetProperty(item => item.ReviewNote, Clean(request.Note))
                .SetProperty(item => item.LastError, (string?)null));
        if (claimed == 0)
            return Conflict(Error("This merge request is no longer awaiting approval."));

        var mergeRequest = await database.PropertyMergeRequests.SingleAsync(item => item.Id == id);
        if (!TryReadVerifiedPlan(mergeRequest, out var plan))
        {
            mergeRequest.Status = MetaGrowPropertyMergeStatus.Failed;
            mergeRequest.LastError = "The saved merge plan failed its integrity check.";
            await database.SaveChangesAsync();
            return Conflict(Error(mergeRequest.LastError));
        }

        var livePreview = await tgsApi.GetPropertyMergePreview(plan!);
        if (livePreview is null || !livePreview.CanRequestMerge)
        {
            mergeRequest.Status = MetaGrowPropertyMergeStatus.Failed;
            mergeRequest.LastError = Truncate(
                tgsApi.ErrorMessage ?? "The live merge plan now has unresolved blocks or data collisions.",
                1000);
            await database.SaveChangesAsync();
            return Conflict(Error(mergeRequest.LastError));
        }

        // Re-canonicalise the saved choices against the current raw property values.
        // This also upgrades requests created before newly exposed exact field differences.
        var executionPlan = CanonicalPlan(plan!, livePreview);
        mergeRequest.PlanJson = JsonSerializer.Serialize(executionPlan, JsonOptions);
        mergeRequest.PlanHash = Hash(mergeRequest.PlanJson);
        return await ExecuteClaimed(mergeRequest, executionPlan, token, reviewerId, reviewerEmail);
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

    private async Task<ActionResult<MetaGrowPropertyMergeRequestDto>> ExecuteClaimed(
        PropertyMergeRequest mergeRequest,
        PropertyMergePreviewRequest plan,
        string token,
        string reviewerId,
        string reviewerEmail)
    {
        var result = await tgsApi.ExecutePropertyMerge(new PropertyMergeExecutionRequest
        {
            RequestId = mergeRequest.Id,
            RequestedByUserId = mergeRequest.RequestedByUserId,
            RequestedByEmail = mergeRequest.RequestedByEmail,
            Plan = plan
        }, token);

        if (result is null)
        {
            mergeRequest.Status = MetaGrowPropertyMergeStatus.Failed;
            mergeRequest.LastError = Truncate(tgsApi.ErrorMessage ?? "The properties could not be merged.", 1000);
            await database.SaveChangesAsync();
            return Conflict(Error(mergeRequest.LastError));
        }

        mergeRequest.Status = MetaGrowPropertyMergeStatus.Completed;
        mergeRequest.ReviewedByUserId = reviewerId;
        mergeRequest.ReviewedByEmail = reviewerEmail;
        mergeRequest.ReviewedUtc ??= DateTime.UtcNow;
        mergeRequest.SourcePropertyName = result.SourcePropertyName;
        mergeRequest.TargetPropertyName = result.TargetPropertyName;
        mergeRequest.PropertyMergeId = result.PropertyMergeId;
        mergeRequest.CompletedUtc = result.CompletedUtc;
        mergeRequest.LastError = null;
        try
        {
            await database.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // The idempotent TGS merge has already committed. Return success and make the
            // missing secondary audit state conspicuous rather than inviting a second merge.
            logger.LogError(exception,
                "TGS property merge {PropertyMergeId} completed but MetaGrow request {RequestId} could not be finalised",
                result.PropertyMergeId,
                mergeRequest.Id);
        }

        return Ok(ToDto(mergeRequest));
    }

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
            LastError = request.LastError,
            PropertyMergeId = request.PropertyMergeId,
            CompletedUtc = request.CompletedUtc
        };
    }

    private static bool TryReadVerifiedPlan(
        PropertyMergeRequest request,
        out PropertyMergePreviewRequest? plan)
    {
        plan = null;
        if (!string.Equals(Hash(request.PlanJson), request.PlanHash, StringComparison.Ordinal)) return false;
        try { plan = JsonSerializer.Deserialize<PropertyMergePreviewRequest>(request.PlanJson, JsonOptions); }
        catch (JsonException) { return false; }
        return plan is not null;
    }

    private string? AccessToken() =>
        AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) &&
        authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? authorization.Parameter
            : null;

    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
